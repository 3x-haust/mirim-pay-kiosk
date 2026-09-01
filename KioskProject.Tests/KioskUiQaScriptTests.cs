using System.Management.Automation.Language;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace KioskProject.Tests;

public sealed class KioskUiQaScriptTests
{
    private static readonly string ScriptPath = Path.Combine(
        ProductAssemblyFixture.RepositoryRoot, "script", "qa", "KioskUiQa.ps1");
    private static readonly string Source = File.ReadAllText(ScriptPath);
    private static readonly ScriptBlockAst Ast = ParseScript();
    private static readonly XNamespace Automation =
        "clr-namespace:System.Windows.Automation;assembly=PresentationCore";

    [Fact]
    public void Script_parses_and_exposes_only_required_paths()
    {
        Assert.NotNull(Ast.ParamBlock);
        var parameterNames = Ast.ParamBlock.Parameters
            .Select(parameter => parameter.Name.VariablePath.UserPath)
            .ToArray();

        Assert.Equal(new[] { "ExePath", "EvidenceDir" }, parameterNames);
        Assert.All(Ast.ParamBlock.Parameters, parameter =>
            Assert.Contains("Mandatory", parameter.Extent.Text, StringComparison.Ordinal));
    }

    [Fact]
    public void Script_embedded_helper_references_all_required_assemblies()
    {
        var addType = Ast.FindAll(
                node => node is CommandAst command &&
                        string.Equals(command.GetCommandName(), "Add-Type", StringComparison.OrdinalIgnoreCase) &&
                        command.Extent.Text.Contains("-ReferencedAssemblies", StringComparison.Ordinal),
                searchNestedScriptBlocks: true)
            .Cast<CommandAst>()
            .Single();

        var arguments = addType.Extent.Text;
        Assert.Contains("UIAutomationClient", arguments, StringComparison.Ordinal);
        Assert.Contains("UIAutomationTypes", arguments, StringComparison.Ordinal);
        Assert.Contains("WindowsBase", arguments, StringComparison.Ordinal);
        Assert.Contains("System.Drawing", arguments, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_uses_event_subscriptions_before_actions_with_bounded_waits()
    {
        Assert.Contains("WindowOpenedEvent", Source, StringComparison.Ordinal);
        Assert.Contains("AddStructureChangedEventHandler", Source, StringComparison.Ordinal);
        Assert.Contains("AddAutomationPropertyChangedEventHandler", Source, StringComparison.Ordinal);
        Assert.Contains("ManualResetEventSlim", Source, StringComparison.Ordinal);
        Assert.Contains("Wait(UiTimeoutMilliseconds)", Source, StringComparison.Ordinal);
        Assert.Contains("RemoveStructureChangedEventHandler", Source, StringComparison.Ordinal);
        Assert.Contains("RemoveAutomationPropertyChangedEventHandler", Source, StringComparison.Ordinal);

        var helper = ExtractTypeMethod("InvokeAndWait");
        Assert.True(helper.IndexOf("AddStructureChangedEventHandler", StringComparison.Ordinal) <
                    helper.IndexOf("invokePattern.Invoke", StringComparison.Ordinal));
        Assert.True(helper.IndexOf("AddAutomationPropertyChangedEventHandler", StringComparison.Ordinal) <
                    helper.IndexOf("invokePattern.Invoke", StringComparison.Ordinal));
        Assert.True(helper.IndexOf("invokePattern.Invoke", StringComparison.Ordinal) <
                    helper.IndexOf("Wait(UiTimeoutMilliseconds)", StringComparison.Ordinal));
    }

    [Fact]
    public void Window_waiter_correlates_pre_and_post_launch_events_by_process_id()
    {
        var waiter = ExtractType("KioskWindowWaiter");

        Assert.Contains("ConcurrentDictionary<int, ConcurrentQueue<AutomationElement>>", waiter, StringComparison.Ordinal);
        Assert.Contains("candidate.Current.ProcessId", waiter, StringComparison.Ordinal);
        Assert.Contains("process.Id", waiter, StringComparison.Ordinal);
        Assert.Contains("TryRemove", waiter, StringComparison.Ordinal);
        Assert.DoesNotContain("Current.Name == \"MIRIM PAY\"", waiter, StringComparison.Ordinal);
        Assert.Contains("out removedSignal", waiter, StringComparison.Ordinal);
        Assert.DoesNotContain("TryRemove(process.Id, out signal)", waiter, StringComparison.Ordinal);
        AssertInTextOrder(ExtractPowerShellFunction("Start-KioskProcess"),
            "[KioskWindowWaiter]::new()", "Start-Process", "WaitForWindow($process)");
    }

    [Fact]
    public void Script_has_no_sleep_polling_or_coordinate_only_input()
    {
        var forbiddenCommands = Ast.FindAll(node => node is CommandAst, searchNestedScriptBlocks: true)
            .Cast<CommandAst>()
            .Select(command => command.GetCommandName())
            .Where(name => name is not null && new[]
            {
                "Start-Sleep", "SendKeys", "Set-CursorPosition", "mouse_event"
            }.Contains(name, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        var pollingLoops = Ast.FindAll(
            node => node is WhileStatementAst or DoWhileStatementAst or DoUntilStatementAst,
            searchNestedScriptBlocks: true);

        Assert.Empty(forbiddenCommands);
        Assert.Empty(pollingLoops);
        Assert.DoesNotContain("Thread.Sleep", Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Happy_path_drives_only_live_stable_automation_ids()
    {
        var drivenIds = Regex.Matches(Source, "::(?:InvokeAndWait|SetValueAndWait)\\(\\$window, \\\"([^\\\"]+)\\\"")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var xamlIds = Directory.GetFiles(Path.Combine(ProductAssemblyFixture.RepositoryRoot, "KioskProject"), "*.xaml", SearchOption.AllDirectories)
            .Select(XDocument.Load)
            .SelectMany(document => document.Descendants())
            .Select(element => (string?)element.Attribute(Automation + "AutomationProperties.AutomationId"))
            .Where(id => id is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            new[] { "BarcodeAddButton", "BarcodeInput", "CompleteButton", "IncreaseButton", "NextButton", "OrderButton", "PayPaymentButton", "PaymentButton" },
            drivenIds.Order(StringComparer.Ordinal));
        Assert.All(drivenIds, id => Assert.Contains(id, xamlIds));
        Assert.DoesNotContain("CartButton", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("MenuCatalogState", Source, StringComparison.Ordinal);
        AssertInOrder("OrderButton", "BarcodeInput", "BarcodeAddButton", "IncreaseButton",
            "PaymentButton", "PayPaymentButton", "NextButton", "CompleteButton");
        Assert.Equal(2, Regex.Matches(Source, "InvokeAndWait\\(\\$window, \\\"OrderButton\\\", \\\"BarcodeInput\\\"", RegexOptions.CultureInvariant).Count);
        Assert.DoesNotContain("InvokeAndWait($window, \"OrderButton\", \"CartViewRoot\"", Source, StringComparison.Ordinal);
        AssertInTextOrder(Source, "OrderButton", "BarcodeInput", "Add-Action \"start-order\" \"Cart\"");
    }

    [Fact]
    public void Capture_normalizes_cursor_and_flushes_compositor_before_screen_copy()
    {
        var capture = ExtractTypeMethod("Capture");

        Assert.Contains("SetCursorPos", capture, StringComparison.Ordinal);
        Assert.Contains("DwmFlush", capture, StringComparison.Ordinal);
        Assert.Contains("source.Left", capture, StringComparison.Ordinal);
        Assert.Contains("source.Top", capture, StringComparison.Ordinal);
        Assert.Contains("GetLastWin32Error", capture, StringComparison.Ordinal);
        Assert.Contains("ThrowExceptionForHR", capture, StringComparison.Ordinal);
        AssertInTextOrder(capture, "SetCursorPos", "DwmFlush", "CopyFromScreen");
    }

    [Fact]
    public void Capture_uses_centered_largest_nine_by_sixteen_source_rectangle()
    {
        var capture = ExtractTypeMethod("GetCaptureRectangle");

        Assert.Contains("Math.Min(bounds.Width, bounds.Height * 9 / 16)", capture, StringComparison.Ordinal);
        Assert.Contains("Math.Min(bounds.Height, bounds.Width * 16 / 9)", capture, StringComparison.Ordinal);
        Assert.Contains("bounds.X + (bounds.Width - width) / 2", capture, StringComparison.Ordinal);
        Assert.Contains("bounds.Y + (bounds.Height - height) / 2", capture, StringComparison.Ordinal);
        Assert.Contains("Math.Round", capture, StringComparison.Ordinal);
        Assert.Equal((656, 0, 608, 1080), CenteredSource(1920, 1080));
        Assert.Equal((0, 0, 1080, 1920), CenteredSource(1080, 1920));
        Assert.DoesNotContain("Desktop must expose the kiosk at exactly 1080x1920", Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_asserts_total_order_and_four_portrait_pngs()
    {
        Assert.Contains("5000\\uC6D0", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("5,000\\uC6D0", Source, StringComparison.Ordinal);
        Assert.Contains("Assert-Order", Source, StringComparison.Ordinal);
        Assert.Contains("totalPrice", Source, StringComparison.Ordinal);
        Assert.Contains("paymentMethod", Source, StringComparison.Ordinal);
        Assert.Contains("quantity", Source, StringComparison.Ordinal);

        var captures = Regex.Matches(Source, "::Capture\\(\\$window, \\$EvidenceDir, \\\"([^\\\"]+\\.png)\\\"")
            .Select(match => match.Groups[1].Value)
            .ToArray();
        Assert.Equal(new[] { "01-menu.png", "02-cart.png", "03-payment.png", "04-success.png" }, captures);
        Assert.Equal(4, Regex.Matches(Source, "::Capture\\(\\$window, \\$visualEvidenceDir, \\\"([^\\\"]+\\.png)\\\"").Count);
        Assert.Contains("figma-visual-fixture", Source, StringComparison.Ordinal);
        Assert.Contains("new Bitmap(1080, 1920", Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Visual_fixture_payload_is_ascii_and_decodes_exact_products()
    {
        var start = Source.IndexOf("Data\\menu.json\"), @'", StringComparison.Ordinal);
        Assert.True(start >= 0);
        start += "Data\\menu.json\"), @'".Length;
        var end = Source.IndexOf("'@)", start, StringComparison.Ordinal);
        Assert.True(end > start);
        var payload = Source[start..end];
        Assert.DoesNotContain(payload, character => character > 127);
        using var document = JsonDocument.Parse(payload);
        var items = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(3, items.Length);
        Assert.Equal("오리온 도도한 나쵸 샤워크림어니언", items[0].GetProperty("name").GetString());
        Assert.Equal("롤리팝 아이스캔디", items[1].GetProperty("name").GetString());
        Assert.Equal("오리온 더 탱글 미구미", items[2].GetProperty("name").GetString());
        Assert.All(items, item => Assert.Equal("간식", item.GetProperty("category").GetString()));
        Assert.Equal(new[] { 1700, 700, 1200 }, items.Select(item => item.GetProperty("price").GetInt32()).ToArray());
        Assert.All(items, item => Assert.Equal(20, item.GetProperty("stock").GetInt32()));
    }

    [Fact]
    public void Visual_fixture_has_exact_items_and_event_first_action_contract()
    {
        Assert.Contains("\\uC624\\uB9AC\\uC628", Source, StringComparison.Ordinal);
        Assert.Contains("\\uB864\\uB9AC\\uD31D", Source, StringComparison.Ordinal);
        Assert.Contains("\\uAC04\\uC2DD", Source, StringComparison.Ordinal);
        Assert.Contains("\"price\":1700", Source, StringComparison.Ordinal);
        Assert.Contains("\"price\":700", Source, StringComparison.Ordinal);
        Assert.Contains("\"price\":1200", Source, StringComparison.Ordinal);
        Assert.Contains("AssertVisualCart", Source, StringComparison.Ordinal);
        var visualCart = ExtractTypeMethod("AssertVisualCart");
        Assert.Contains("StateReached(window, null, VisualFooterCount", visualCart, StringComparison.Ordinal);
        Assert.Contains("StateReached(window, null, VisualTotal", visualCart, StringComparison.Ordinal);
        Assert.Contains("VisualCartCountFour", Source, StringComparison.Ordinal);
        Assert.Contains("VisualFooterCount = \"4\\uAC1C\"", Source, StringComparison.Ordinal);
        Assert.Contains("VisualTotal = \"4300\\uC6D0\"", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("4200\\uC6D0", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("4,200\\uC6D0", Source, StringComparison.Ordinal);
        Assert.Contains("count-4-total-4300", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("total-4200", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("InvokeAndWait($window, \"BarcodeAddButton\", $null, $null, -1)", Source, StringComparison.Ordinal);
        AssertInTextOrder(Source, "barcode = \"1\"; expectedCount = [KioskUiQa]::VisualCartCountOne", "barcode = \"2\"; expectedCount = [KioskUiQa]::VisualCartCountTwo", "barcode = \"2\"; expectedCount = [KioskUiQa]::VisualCartCountThree", "barcode = \"3\"; expectedCount = [KioskUiQa]::VisualCartCountFour");
        Assert.DoesNotContain("CartButton", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("MenuCatalogState", Source, StringComparison.Ordinal);
        AssertInTextOrder(Source, "SetValueAndWait($window, \"BarcodeInput\", $barcode)", "InvokeAndWait($window, \"BarcodeAddButton\", $null, $expectedCount, -1)");
        AssertInTextOrder(Source, "visualEvidenceDir", "visual-menu", "visual-start-order", "visual-barcode-add", "visual-show-cart", "visual-select-pay", "visual-save-order");
        Assert.Equal(4, Regex.Matches(Source, "expectedCount = \\[KioskUiQa\\]::VisualCartCount", RegexOptions.CultureInvariant).Count);
        Assert.Contains("Assert-Order $ordersPath", Source, StringComparison.Ordinal);
        Assert.Contains("total-5000", Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Failure_fixture_and_cleanup_are_mandatory()
    {
        Assert.Contains("malformed", Source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AssertEnabled($window, \"OrderButton\", $false)", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("AssertLoadError", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadError", Source, StringComparison.Ordinal);
        Assert.Contains("finally", Source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Stop-KioskProcess", Source, StringComparison.Ordinal);
        Assert.Contains("Remove-Item -LiteralPath $qaTempRoot -Recurse -Force", Source, StringComparison.Ordinal);
        Assert.Contains("cleanup-receipt.json", Source, StringComparison.Ordinal);
        Assert.Contains("processAbsent", Source, StringComparison.Ordinal);
        Assert.Contains("tempDirectoryAbsent", Source, StringComparison.Ordinal);
        Assert.Contains("fixtureRestored", Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Malformed_menu_flow_keeps_menu_and_disables_order_without_error_page_or_copy()
    {
        // Given
        var flow = Source[Source.IndexOf("$malformed = Start-KioskProcess", StringComparison.Ordinal)..
            Source.IndexOf("Stop-KioskProcess $malformed", StringComparison.Ordinal)];

        // When
        var malformedFlow = flow;

        // Then
        Assert.DoesNotContain("AssertLoadError", malformedFlow, StringComparison.Ordinal);
        Assert.DoesNotContain("MenuViewRoot", malformedFlow, StringComparison.Ordinal);
        Assert.Contains("AssertVisible($window, \"OrderButton\")", malformedFlow, StringComparison.Ordinal);
        Assert.Contains("AssertEnabled($window, \"OrderButton\", $false)", malformedFlow, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentViewRoot", malformedFlow, StringComparison.Ordinal);
        Assert.DoesNotContain("error-visible", malformedFlow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("menu-visible-order-disabled", malformedFlow, StringComparison.Ordinal);
    }

    [Fact]
    public void Cleanup_sweeps_only_live_kiosk_processes_under_the_qa_temp_root()
    {
        Assert.Contains("GetProcessesByName", Source, StringComparison.Ordinal);
        Assert.Contains("$qaTempRoot", Source, StringComparison.Ordinal);
        Assert.Contains(".Path", Source, StringComparison.Ordinal);
        Assert.Contains("Kill()", Source, StringComparison.Ordinal);
        Assert.Contains("WaitForExit(5000)", Source, StringComparison.Ordinal);
        Assert.Contains("Dispose()", Source, StringComparison.Ordinal);
        Assert.Contains("cleanupErrors.Add", Source, StringComparison.Ordinal);
        AssertInTextOrder(Source, "GetProcessesByName", "trackedPids", "Remove-Item -LiteralPath $qaTempRoot");
        Assert.Contains("remainingPids = @($trackedPids", Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_started_process_is_tracked_before_wait_and_independently_proven_absent()
    {
        var start = ExtractPowerShellFunction("Start-KioskProcess");
        AssertInTextOrder(start,
            "[KioskWindowWaiter]::new()", "Start-Process", "$script:trackedProcesses.Add($process)",
            "$script:trackedPids.Add($process.Id)", "WaitForWindow($process)");
        Assert.DoesNotContain("Process.Start(", Source, StringComparison.Ordinal);

        var stop = ExtractPowerShellFunction("Stop-TrackedProcess");
        AssertInTextOrder(stop, "$Process.Kill()", "$Process.WaitForExit(5000)");
        Assert.Contains("GetProcessById", Source, StringComparison.Ordinal);
        Assert.Contains("$remainingPids", Source, StringComparison.Ordinal);
        Assert.Contains("trackedPids = @($trackedPids)", Source, StringComparison.Ordinal);
        Assert.Contains("remainingPids = @($remainingPids)", Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Validated_order_is_copied_and_hashed_before_temp_cleanup()
    {
        Assert.Contains("validated-order.json", Source, StringComparison.Ordinal);
        Assert.Contains("evidence-manifest.json", Source, StringComparison.Ordinal);
        AssertInTextOrder(Source,
            "Assert-Order $ordersPath",
            "Copy-Item -LiteralPath $ordersPath -Destination $validatedOrderPath",
            "Get-FileHash -LiteralPath $validatedOrderPath -Algorithm SHA256",
            "persist-order-evidence",
            "Remove-Item -LiteralPath $qaTempRoot -Recurse -Force");
        Assert.Contains("path = \"validated-order.json\"", Source, StringComparison.Ordinal);
        Assert.Contains("sha256 = $validatedOrderHash", Source, StringComparison.Ordinal);
    }

    private static ScriptBlockAst ParseScript()
    {
        var ast = Parser.ParseFile(ScriptPath, out _, out var errors);
        Assert.Empty(errors);
        return ast;
    }

    private static string ExtractType(string typeName)
    {
        var match = Regex.Match(Source,
            $@"public sealed class {typeName}\b[\s\S]*?(?=public static class)",
            RegexOptions.CultureInvariant);
        Assert.True(match.Success, $"Missing helper type {typeName}.");
        return match.Value;
    }

    private static string ExtractTypeMethod(string methodName)
    {
        var match = Regex.Match(Source,
            $@"public static [^ ]+ {methodName}\b[\s\S]*?^    }}",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);
        Assert.True(match.Success, $"Missing helper method {methodName}.");
        return match.Value;
    }

    private static string ExtractPowerShellFunction(string functionName)
    {
        var function = Ast.FindAll(
                node => node is FunctionDefinitionAst definition && definition.Name == functionName,
                searchNestedScriptBlocks: true)
            .Cast<FunctionDefinitionAst>()
            .SingleOrDefault();
        Assert.NotNull(function);
        return function.Extent.Text;
    }

    private static (int X, int Y, int Width, int Height) CenteredSource(int width, int height)
    {
        var sourceWidth = (int)Math.Round(Math.Min(width, height * 9d / 16d));
        var sourceHeight = (int)Math.Round(Math.Min(height, width * 16d / 9d));
        return ((width - sourceWidth) / 2, (height - sourceHeight) / 2, sourceWidth, sourceHeight);
    }

    private static void AssertInTextOrder(string text, params string[] values)
    {
        var cursor = -1;
        foreach (var value in values)
        {
            cursor = text.IndexOf(value, cursor + 1, StringComparison.Ordinal);
            Assert.True(cursor >= 0, $"Missing ordered contract {value}.");
        }
    }

    private static void AssertInOrder(params string[] values)
    {
        var cursor = -1;
        foreach (var value in values)
        {
            cursor = Source.IndexOf($"\"{value}\"", cursor + 1, StringComparison.Ordinal);
            Assert.True(cursor >= 0, $"Missing ordered action {value}.");
        }
    }
}
