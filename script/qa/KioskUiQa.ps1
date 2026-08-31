# allow: SIZE_OK - the standalone QA entrypoint embeds its UIA helper so the exact invocation needs no deployed helper files.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ExePath,

    [Parameter(Mandatory = $true)]
    [string]$EvidenceDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($env:OS -ne "Windows_NT") {
    throw "Kiosk UI QA requires an interactive Windows desktop."
}

$ExePath = [IO.Path]::GetFullPath($ExePath)
$EvidenceDir = [IO.Path]::GetFullPath($EvidenceDir)
if (-not [IO.File]::Exists($ExePath)) {
    throw "Published executable not found: $ExePath"
}

[IO.Directory]::CreateDirectory($EvidenceDir) | Out-Null
$artifactNames = @(
    "01-menu.png", "02-cart.png", "03-payment.png", "04-success.png",
    "action-log.json", "task-10-mirim-pay-kiosk-wpf.log", "cleanup-receipt.json",
    "validated-order.json", "evidence-manifest.json"
)
foreach ($artifactName in $artifactNames) {
    $artifactPath = Join-Path $EvidenceDir $artifactName
    if (Test-Path -LiteralPath $artifactPath) {
        Remove-Item -LiteralPath $artifactPath -Force
    }
}

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies @("UIAutomationClient", "UIAutomationTypes", "WindowsBase", "System.Drawing") -TypeDefinition @'
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Automation;

public sealed class KioskUiContext
{
    public readonly Process Process;
    public readonly AutomationElement Window;

    public KioskUiContext(Process process, AutomationElement window)
    {
        Process = process;
        Window = window;
    }
}

public sealed class KioskWindowWaiter : IDisposable
{
    private readonly ConcurrentDictionary<int, ConcurrentQueue<AutomationElement>> candidates =
        new ConcurrentDictionary<int, ConcurrentQueue<AutomationElement>>();
    private readonly ConcurrentDictionary<int, ManualResetEventSlim> signals =
        new ConcurrentDictionary<int, ManualResetEventSlim>();
    private readonly AutomationEventHandler handler;

    public KioskWindowWaiter()
    {
        handler = delegate(object sender, AutomationEventArgs args)
        {
            AutomationElement candidate = sender as AutomationElement;
            try
            {
                if (candidate != null)
                {
                    int processId = candidate.Current.ProcessId;
                    candidates.GetOrAdd(processId, ignored => new ConcurrentQueue<AutomationElement>()).Enqueue(candidate);
                    ManualResetEventSlim signal;
                    if (signals.TryGetValue(processId, out signal)) signal.Set();
                }
            }
            catch (ElementNotAvailableException) { }
        };
        Automation.AddAutomationEventHandler(
            WindowPattern.WindowOpenedEvent, AutomationElement.RootElement,
            TreeScope.Descendants, handler);
    }

    public AutomationElement WaitForWindow(Process process)
    {
        ConcurrentQueue<AutomationElement> queue;
        if (!candidates.TryGetValue(process.Id, out queue))
        {
            queue = candidates.GetOrAdd(process.Id, ignored => new ConcurrentQueue<AutomationElement>());
        }
        AutomationElement window;
        if (!queue.TryDequeue(out window))
        {
            using (ManualResetEventSlim signal = new ManualResetEventSlim(false))
            {
                signals[process.Id] = signal;
                if (!queue.TryDequeue(out window) &&
                    (!signal.Wait(KioskUiQa.UiTimeoutMilliseconds) || !queue.TryDequeue(out window)))
                    throw new TimeoutException("The launched process window did not open before the bounded timeout.");
                ManualResetEventSlim removedSignal;
                signals.TryRemove(process.Id, out removedSignal);
            }
        }
        if (window.Current.ProcessId != process.Id)
            throw new InvalidOperationException("WindowOpenedEvent did not identify the launched process.");
        candidates.TryRemove(process.Id, out queue);
        return window;
    }

    public void Dispose()
    {
        Automation.RemoveAutomationEventHandler(
            WindowPattern.WindowOpenedEvent, AutomationElement.RootElement, handler);
        foreach (ManualResetEventSlim signal in signals.Values) signal.Dispose();
        signals.Clear();
        candidates.Clear();
    }
}

public static class KioskUiQa
{
    public const int UiTimeoutMilliseconds = 15000;
    public const string CartCountOne = "\uC7A5\uBC14\uAD6C\uB2C8 1\uAC1C";
    public const string ExpectedTotal = "5,000\uC6D0";
    private const string LoadError = "\uC0C1\uD488 \uC815\uBCF4\uB97C \uBD88\uB7EC\uC624\uC9C0 \uBABB\uD588\uC2B5\uB2C8\uB2E4.";

    public static void SetValueAndWait(AutomationElement window, string automationId, string value)
    {
        AutomationElement source = FindById(window, automationId);
        using (ManualResetEventSlim changed = new ManualResetEventSlim(false))
        {
            AutomationPropertyChangedEventHandler handler = delegate(object sender, AutomationPropertyChangedEventArgs args)
            {
                AutomationElement element = sender as AutomationElement;
                if (element != null && args.Property == ValuePattern.ValueProperty &&
                    String.Equals(args.NewValue as string, value, StringComparison.Ordinal)) changed.Set();
            };
            Automation.AddAutomationPropertyChangedEventHandler(
                source, TreeScope.Element, handler, ValuePattern.ValueProperty);
            try
            {
                ValuePattern valuePattern = (ValuePattern)source.GetCurrentPattern(ValuePattern.Pattern);
                valuePattern.SetValue(value);
                if (!changed.Wait(UiTimeoutMilliseconds))
                    throw new TimeoutException("ValueProperty did not change before the bounded timeout.");
            }
            finally
            {
                Automation.RemoveAutomationPropertyChangedEventHandler(source, handler);
            }
        }
    }

    public static void InvokeAndWait(
        AutomationElement window, string sourceId, string expectedId, string expectedName, int expectedEnabled)
    {
        AutomationElement source = FindById(window, sourceId);
        using (ManualResetEventSlim reached = new ManualResetEventSlim(false))
        {
            StructureChangedEventHandler structureHandler = delegate(object sender, StructureChangedEventArgs args)
            {
                if (StateReached(window, expectedId, expectedName, expectedEnabled)) reached.Set();
            };
            AutomationPropertyChangedEventHandler propertyHandler = delegate(object sender, AutomationPropertyChangedEventArgs args)
            {
                if (StateReached(window, expectedId, expectedName, expectedEnabled)) reached.Set();
            };
            Automation.AddStructureChangedEventHandler(window, TreeScope.Subtree, structureHandler);
            Automation.AddAutomationPropertyChangedEventHandler(
                window, TreeScope.Subtree, propertyHandler,
                AutomationElement.IsOffscreenProperty, AutomationElement.IsEnabledProperty,
                AutomationElement.NameProperty);
            try
            {
                InvokePattern invokePattern = (InvokePattern)source.GetCurrentPattern(InvokePattern.Pattern);
                invokePattern.Invoke();
                if (!reached.Wait(UiTimeoutMilliseconds))
                    throw new TimeoutException("Expected UI state did not arrive before the bounded timeout.");
            }
            finally
            {
                Automation.RemoveStructureChangedEventHandler(window, structureHandler);
                Automation.RemoveAutomationPropertyChangedEventHandler(window, propertyHandler);
            }
            if (!StateReached(window, expectedId, expectedName, expectedEnabled))
                throw new InvalidOperationException("The expected UI state was lost after its event.");
        }
    }

    public static void AssertVisible(AutomationElement window, string automationId)
    {
        AutomationElement element = FindById(window, automationId);
        if (element.Current.IsOffscreen) throw new InvalidOperationException(automationId + " is offscreen.");
    }

    public static void AssertEnabled(AutomationElement window, string automationId, bool expected)
    {
        AutomationElement element = FindById(window, automationId);
        if (element.Current.IsEnabled != expected)
            throw new InvalidOperationException(automationId + " has the wrong enabled state.");
    }

    public static void AssertTotal(AutomationElement window)
    {
        if (!StateReached(window, null, ExpectedTotal, -1))
            throw new InvalidOperationException("The expected order total is not visible.");
    }

    public static void AssertLoadError(AutomationElement window)
    {
        if (!StateReached(window, null, LoadError, -1))
            throw new InvalidOperationException("The malformed menu error is not visible.");
    }

    public static Rectangle GetCaptureRectangle(Rectangle bounds)
    {
        double sourceWidth = Math.Min(bounds.Width, bounds.Height * 9 / 16);
        double sourceHeight = Math.Min(bounds.Height, bounds.Width * 16 / 9);
        int width = Convert.ToInt32(Math.Round(sourceWidth));
        int height = Convert.ToInt32(Math.Round(sourceHeight));
        int x = bounds.X + (bounds.Width - width) / 2;
        int y = bounds.Y + (bounds.Height - height) / 2;
        return new Rectangle(x, y, width, height);
    }

    public static void Capture(AutomationElement window, string evidenceDir, string fileName)
    {
        System.Windows.Rect bounds = window.Current.BoundingRectangle;
        Rectangle desktop = new Rectangle(
            Convert.ToInt32(Math.Round(bounds.Left)), Convert.ToInt32(Math.Round(bounds.Top)),
            Convert.ToInt32(Math.Round(bounds.Width)), Convert.ToInt32(Math.Round(bounds.Height)));
        Rectangle source = GetCaptureRectangle(desktop);
        using (Bitmap captured = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb))
        using (Graphics captureGraphics = Graphics.FromImage(captured))
        using (Bitmap portrait = new Bitmap(1080, 1920, PixelFormat.Format32bppArgb))
        using (Graphics graphics = Graphics.FromImage(portrait))
        using (ImageAttributes attributes = new ImageAttributes())
        {
            captureGraphics.CopyFromScreen(source.Location, Point.Empty, source.Size);
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            attributes.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY);
            graphics.DrawImage(captured, new Rectangle(0, 0, portrait.Width, portrait.Height),
                0, 0, captured.Width, captured.Height, GraphicsUnit.Pixel, attributes);
            portrait.Save(Path.Combine(evidenceDir, fileName), ImageFormat.Png);
        }
    }

    public static void Close(KioskUiContext context)
    {
        if (context == null || context.Process.HasExited) return;
        try
        {
            WindowPattern pattern = (WindowPattern)context.Window.GetCurrentPattern(WindowPattern.Pattern);
            pattern.Close();
        }
        catch (ElementNotAvailableException) { }
        catch (InvalidOperationException) { }
        if (!context.Process.WaitForExit(5000))
        {
            context.Process.Kill();
            if (!context.Process.WaitForExit(5000))
                throw new TimeoutException("Kiosk process survived close and kill.");
        }
    }

    public static bool IsProcessAbsent(int processId)
    {
        try
        {
            using (Process process = Process.GetProcessById(processId))
                return process.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static AutomationElement FindById(AutomationElement root, string automationId)
    {
        AutomationElement element = root.FindFirst(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));
        if (element == null) throw new InvalidOperationException("AutomationId not found: " + automationId);
        return element;
    }

    private static bool StateReached(
        AutomationElement root, string expectedId, string expectedName, int expectedEnabled)
    {
        try
        {
            Condition condition = expectedId != null
                ? (Condition)new PropertyCondition(AutomationElement.AutomationIdProperty, expectedId)
                : new PropertyCondition(AutomationElement.NameProperty, expectedName);
            AutomationElement element = root.FindFirst(TreeScope.Descendants, condition);
            return element != null && !element.Current.IsOffscreen &&
                (expectedEnabled < 0 || element.Current.IsEnabled == (expectedEnabled == 1));
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }
}
'@

function Add-Action([string]$Name, [string]$State) {
    $script:actions.Add([pscustomobject]@{
        sequence = $script:actions.Count + 1
        timestampUtc = [DateTime]::UtcNow.ToString("O")
        action = $Name
        state = $State
    })
}

function Start-KioskProcess([string]$Path) {
    $waiter = [KioskWindowWaiter]::new()
    try {
        $process = Start-Process -FilePath $Path -WorkingDirectory (Split-Path -Parent $Path) -PassThru
        $script:trackedProcesses.Add($process)
        $script:trackedPids.Add($process.Id)
        Add-Action "start-process" "pid=$($process.Id)"
        $window = $waiter.WaitForWindow($process)
        $context = [KioskUiContext]::new($process, $window)
        $script:contexts.Add($context)
        return $context
    }
    finally {
        $waiter.Dispose()
    }
}

function Stop-KioskProcess([KioskUiContext]$Context) {
    if ($null -ne $Context) {
        [KioskUiQa]::Close($Context)
    }
}

function Stop-TrackedProcess([Diagnostics.Process]$Process) {
    if (-not $Process.HasExited) {
        $Process.Kill()
        if (-not $Process.WaitForExit(5000)) {
            throw "Tracked kiosk process survived the bounded kill wait."
        }
    }
}

function Assert-Order([string]$OrdersPath) {
    $orders = @((Get-Content -LiteralPath $OrdersPath -Raw | ConvertFrom-Json))
    if ($orders.Count -ne 1) { throw "Expected exactly one saved order." }
    $order = $orders[0]
    if ($order.totalPrice -ne 5000 -or $order.paymentMethod -ne "Pay") {
        throw "Saved order total or payment method is incorrect."
    }
    if (@($order.items).Count -ne 1 -or $order.items[0].quantity -ne 2) {
        throw "Saved order item quantity is incorrect."
    }
}

$actions = [Collections.Generic.List[object]]::new()
$contexts = [Collections.Generic.List[KioskUiContext]]::new()
$trackedProcesses = [Collections.Generic.List[Diagnostics.Process]]::new()
$trackedPids = [Collections.Generic.List[int]]::new()
$validatedOrderPath = Join-Path $EvidenceDir "validated-order.json"
$validatedOrderHash = $null
$qaTempRoot = Join-Path ([IO.Path]::GetTempPath()) ("mirim-kiosk-uiqa-" + [Guid]::NewGuid().ToString("N"))
$sourceRoot = Split-Path -Parent $ExePath
$sourceMenu = Join-Path $sourceRoot "Data\menu.json"
$sourceOrders = Join-Path $sourceRoot "Data\orders.json"
$fixtureHashes = @{}
foreach ($fixturePath in @($sourceMenu, $sourceOrders)) {
    if (-not (Test-Path -LiteralPath $fixturePath -PathType Leaf)) {
        throw "Published fixture not found: $fixturePath"
    }
    $fixtureHashes[$fixturePath] = (Get-FileHash -LiteralPath $fixturePath -Algorithm SHA256).Hash
}

$failure = $null
$cleanupErrors = [Collections.Generic.List[string]]::new()
try {
    [IO.Directory]::CreateDirectory($qaTempRoot) | Out-Null
    $happyRoot = Join-Path $qaTempRoot "happy"
    $failureRoot = Join-Path $qaTempRoot "failure"
    Copy-Item -LiteralPath $sourceRoot -Destination $happyRoot -Recurse
    Copy-Item -LiteralPath $sourceRoot -Destination $failureRoot -Recurse
    [IO.File]::WriteAllText((Join-Path $happyRoot "Data\orders.json"), "[]")
    [IO.File]::WriteAllText((Join-Path $failureRoot "Data\menu.json"), '{"malformed":')
    [IO.File]::WriteAllText((Join-Path $failureRoot "Data\orders.json"), "[]")

    $happyExe = Join-Path $happyRoot ([IO.Path]::GetFileName($ExePath))
    $happy = Start-KioskProcess $happyExe
    $window = $happy.Window
    [KioskUiQa]::AssertVisible($window, "OrderButton")
    [KioskUiQa]::Capture($window, $EvidenceDir, "01-menu.png")
    Add-Action "launch" "MenuStartState"

    [KioskUiQa]::InvokeAndWait($window, "OrderButton", "BarcodeInput", $null, -1)
    Add-Action "start-order" "MenuCatalogState"
    [KioskUiQa]::SetValueAndWait($window, "BarcodeInput", "1")
    Add-Action "barcode-input" "1"
    [KioskUiQa]::InvokeAndWait($window, "BarcodeAddButton", $null, [KioskUiQa]::CartCountOne, -1)
    Add-Action "barcode-add" "cart-count-1"
    [KioskUiQa]::InvokeAndWait($window, "CartButton", "IncreaseButton", $null, -1)
    Add-Action "show-cart" "CartViewRoot"
    [KioskUiQa]::InvokeAndWait($window, "IncreaseButton", $null, [KioskUiQa]::ExpectedTotal, -1)
    [KioskUiQa]::AssertTotal($window)
    [KioskUiQa]::Capture($window, $EvidenceDir, "02-cart.png")
    Add-Action "increase-quantity" "total-5000"

    [KioskUiQa]::InvokeAndWait($window, "PaymentButton", "PayPaymentButton", $null, -1)
    [KioskUiQa]::Capture($window, $EvidenceDir, "03-payment.png")
    Add-Action "show-payment" "PaymentSelectionState"
    [KioskUiQa]::InvokeAndWait($window, "PayPaymentButton", "NextButton", $null, 1)
    Add-Action "select-pay" "NextButton-enabled"
    [KioskUiQa]::InvokeAndWait($window, "NextButton", "CompleteButton", $null, -1)
    [KioskUiQa]::Capture($window, $EvidenceDir, "04-success.png")
    Add-Action "save-order" "PaymentSuccessState"
    $ordersPath = Join-Path $happyRoot "Data\orders.json"
    Assert-Order $ordersPath
    Copy-Item -LiteralPath $ordersPath -Destination $validatedOrderPath
    $validatedOrderHash = (Get-FileHash -LiteralPath $validatedOrderPath -Algorithm SHA256).Hash
    Add-Action "persist-order-evidence" "path=validated-order.json sha256=$validatedOrderHash"
    Add-Action "assert-order" "quantity-2-total-5000-pay"
    [KioskUiQa]::InvokeAndWait($window, "CompleteButton", "OrderButton", $null, -1)
    Add-Action "complete" "MenuStartState"
    Stop-KioskProcess $happy

    $failureExe = Join-Path $failureRoot ([IO.Path]::GetFileName($ExePath))
    $malformed = Start-KioskProcess $failureExe
    $window = $malformed.Window
    [KioskUiQa]::AssertVisible($window, "OrderButton")
    [KioskUiQa]::AssertLoadError($window)
    [KioskUiQa]::AssertEnabled($window, "OrderButton", $false)
    Add-Action "malformed-menu" "error-visible-order-disabled"
    Stop-KioskProcess $malformed
}
catch {
    $failure = $_
    Add-Action "failure" $_.Exception.Message
}
finally {
    foreach ($context in $contexts) {
        try { Stop-KioskProcess $context } catch { $cleanupErrors.Add($_.Exception.Message) }
    }
    foreach ($process in $trackedProcesses) {
        try { Stop-TrackedProcess $process } catch { $cleanupErrors.Add($_.Exception.Message) }
    }
    try {
        if (Test-Path -LiteralPath $qaTempRoot) {
            Remove-Item -LiteralPath $qaTempRoot -Recurse -Force
        }
    }
    catch { $cleanupErrors.Add($_.Exception.Message) }

    $remainingPids = @($trackedPids | Where-Object { -not [KioskUiQa]::IsProcessAbsent($_) })
    $processAbsent = $remainingPids.Count -eq 0
    $tempDirectoryAbsent = -not (Test-Path -LiteralPath $qaTempRoot)
    $fixtureRestored = @($fixtureHashes.Keys | Where-Object {
        -not (Test-Path -LiteralPath $_ -PathType Leaf) -or
        (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash -ne $fixtureHashes[$_]
    }).Count -eq 0
    if (-not $processAbsent) { $cleanupErrors.Add("A launched kiosk process remains.") }
    if (-not $tempDirectoryAbsent) { $cleanupErrors.Add("The QA temp directory remains.") }
    if (-not $fixtureRestored) { $cleanupErrors.Add("A published fixture changed.") }

    $receipt = [pscustomobject]@{
        processAbsent = $processAbsent
        trackedPids = @($trackedPids)
        remainingPids = @($remainingPids)
        tempDirectoryAbsent = $tempDirectoryAbsent
        fixtureRestored = $fixtureRestored
        cleanupErrors = @($cleanupErrors)
        completedUtc = [DateTime]::UtcNow.ToString("O")
    }
    $orderArtifact = if ($null -eq $validatedOrderHash) { $null } else {
        [pscustomobject]@{
            path = "validated-order.json"
            sha256 = $validatedOrderHash
        }
    }
    $manifest = [pscustomobject]@{
        validatedOrder = $orderArtifact
        actionLog = "action-log.json"
        cleanupReceipt = "cleanup-receipt.json"
    }
    $receipt | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $EvidenceDir "cleanup-receipt.json") -Encoding UTF8
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $EvidenceDir "evidence-manifest.json") -Encoding UTF8
    $actions | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $EvidenceDir "action-log.json") -Encoding UTF8
    $actions | ForEach-Object { "{0:D2} {1} {2}" -f $_.sequence, $_.action, $_.state } |
        Set-Content -LiteralPath (Join-Path $EvidenceDir "task-10-mirim-pay-kiosk-wpf.log") -Encoding UTF8
}

if ($cleanupErrors.Count -gt 0) {
    throw "Kiosk UI QA cleanup failed: $($cleanupErrors -join '; ')"
}
if ($null -ne $failure) {
    throw $failure
}
Write-Output "Kiosk UI QA passed. Evidence: $EvidenceDir"
