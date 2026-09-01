using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace KioskProject.Tests;

public sealed class KioskViewXamlTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Automation = "clr-namespace:System.Windows.Automation;assembly=PresentationCore";
    private static readonly string ViewsRoot = Path.Combine(ProductAssemblyFixture.RepositoryRoot, "KioskProject", "Views");

    private static readonly IReadOnlyDictionary<string, string[]> RequiredAutomationIds =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["MenuView.xaml"] =
            [
                "MenuViewRoot", "MenuStartState", "OrderButton"
            ],
            ["CartView.xaml"] =
            [
                "CartViewRoot", "CartBarcodeState", "BarcodeInput", "BarcodeAddButton", "RemoveButton", "DecreaseButton",
                "IncreaseButton", "PaymentButton", "DemoAddProductButton"
            ],
            ["PaymentView.xaml"] =
            [
                "PaymentViewRoot", "PaymentSelectionState", "PaymentSuccessState",
                "PayPaymentButton", "FacePaymentButton", "BackButton", "NextButton", "CompleteButton"
            ]
        };

    [Fact]
    public void Exactly_three_product_views_use_stable_roots_and_the_portrait_host()
    {
        var viewFiles = Directory.GetFiles(ViewsRoot, "*View.xaml")
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "CartView.xaml", "MenuView.xaml", "PaymentView.xaml" }, viewFiles);

        foreach (var fileName in viewFiles)
        {
            var document = Load(fileName!);
            Assert.Equal("UserControl", document.Root?.Name.LocalName);
            Assert.Contains(document.Descendants(Presentation + "ContentControl"), element =>
                (string?)element.Attribute("Style") == "{StaticResource KioskPortraitHostStyle}");
            Assert.Contains(document.Descendants(), element =>
                (string?)element.Attribute(Automation + "AutomationProperties.AutomationId") == Path.GetFileNameWithoutExtension(fileName) + "Root");
        }
    }

    [Fact]
    public void Required_state_roots_and_interactive_automation_ids_are_present_once()
    {
        var allIds = new List<string>();
        foreach (var (fileName, expectedIds) in RequiredAutomationIds)
        {
            var ids = Load(fileName).Root!.DescendantsAndSelf()
                .Select(element => (string?)element.Attribute(Automation + "AutomationProperties.AutomationId"))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Cast<string>()
                .ToArray();

            foreach (var expectedId in expectedIds)
            {
                Assert.Equal(1, ids.Count(id => id == expectedId));
            }

            allIds.AddRange(ids);
        }

        var duplicateIds = allIds.GroupBy(id => id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1 && !new[] { "AddButton", "RemoveButton", "DecreaseButton", "IncreaseButton" }.Contains(group.Key))
            .Select(group => group.Key)
            .ToArray();
        Assert.Empty(duplicateIds);
    }

    [Fact]
    public void Payment_view_keeps_save_failures_on_the_selection_frame()
    {
        // Given
        var source = Source("PaymentView.xaml");

        // When
        var document = Load("PaymentView.xaml");

        // Then
        Assert.DoesNotContain("PaymentErrorState", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentError", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentErrorState", document.ToString(SaveOptions.DisableFormatting), StringComparison.Ordinal);
    }

    [Fact]
    public void Menu_view_contains_only_the_Figma_start_state()
    {
        var source = Source("MenuView.xaml");
        var document = Load("MenuView.xaml");
        var ids = document.Descendants()
            .Select(element => (string?)element.Attribute(Automation + "AutomationProperties.AutomationId"))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .ToArray();

        Assert.Contains("MenuStartState", ids);
        Assert.DoesNotContain("MenuCatalogState", ids);
        Assert.DoesNotContain("MenuEmptyState", ids);
        Assert.DoesNotContain("AddButton", ids);
        Assert.DoesNotContain("CartButton", ids);
        Assert.DoesNotContain("IsStartScreen", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Categories", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FilteredMenus", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BarcodeInput", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BarcodeAddButton", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Menu_view_binds_start_command_and_non_page_load_messages()
    {
        var source = Source("MenuView.xaml");
        AssertBindingPaths(source,
            "Menu.CanPurchase", "StartOrderCommand");
    }

    [Fact]
    public void Cart_view_binds_shared_items_quantity_remove_count_total_and_payment()
    {
        var source = Source("CartView.xaml");
        AssertBindingPaths(source,
            "Menu", "BarcodeInput", "ScanBarcodeCommand", "Cart.CartItems", "Menu.Name", "Menu.Price", "Quantity", "Subtotal",
            "DataContext.Cart.RemoveCommand", "DataContext.Cart.DecreaseCommand", "DataContext.Cart.IncreaseCommand",
            "Cart.TotalQuantity", "Cart.TotalPrice", "ShowPaymentCommand");

        Assert.Contains("Key=\"Enter\"", source, StringComparison.Ordinal);
        Assert.True(Regex.Matches(source, "CommandParameter=\\\"{Binding}\\\"").Count >= 3);
        Assert.Contains("Text=\"{Binding Cart.TotalPrice, StringFormat={}{0}원}\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Cart.TotalPrice, StringFormat={}{0:N0}원", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Cart_empty_body_exposes_one_demo_product_button_with_contract_binding_and_layout()
    {
        // Given
        var source = Source("CartView.xaml");
        var document = Load("CartView.xaml");

        // When
        var buttons = document.Descendants(Presentation + "Button")
            .Where(element => (string?)element.Attribute(Automation + "AutomationProperties.AutomationId") == "DemoAddProductButton")
            .ToArray();

        // Then
        var button = Assert.Single(buttons);
        Assert.Equal("{Binding Menu.AddDemoProductCommand}", button.Attribute("Command")?.Value);
        Assert.Equal("시연 상품 추가", button.Attribute("Content")?.Value);
        Assert.Equal("2", button.Attribute("Grid.Row")?.Value);
        Assert.Equal("Center", button.Attribute("HorizontalAlignment")?.Value);
        Assert.Equal("Bottom", button.Attribute("VerticalAlignment")?.Value);
        Assert.Equal("300", button.Attribute("Width")?.Value);
        Assert.Equal("96", button.Attribute("Height")?.Value);
        Assert.Equal("{StaticResource KioskTypeLabel}", button.Attribute("FontSize")?.Value);
        Assert.Contains(button.Descendants(Presentation + "Style"), element =>
            element.Attribute("BasedOn")?.Value == "{StaticResource KioskSecondaryButtonStyle}");
        Assert.Matches(@"0,0,0,[1-9][0-9]*", button.Attribute("Margin")?.Value ?? string.Empty);
        Assert.Contains("Cart.CartItems.Count", source, StringComparison.Ordinal);
        Assert.Contains("Collapsed", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Payment_view_exposes_selection_and_success_with_order_summary_commands()
    {
        var source = Source("PaymentView.xaml");
        AssertBindingPaths(source,
            "IsPaymentSuccessful", "SelectedPaymentMethod", "Cart.TotalQuantity", "Cart.TotalPrice",
            "SelectPaymentMethodCommand", "BackToCartCommand", "CompletePaymentCommand", "AcknowledgePaymentCommand");

        Assert.Contains("CommandParameter=\"Pay\"", source, StringComparison.Ordinal);
        Assert.Contains("CommandParameter=\"Face\"", source, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Cart.TotalPrice, StringFormat={}{0}원}\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Cart.TotalPrice, StringFormat={}{0:N0}원", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_interactive_control_declares_a_stable_automation_id()
    {
        foreach (var fileName in RequiredAutomationIds.Keys)
        {
            var interactiveControls = Load(fileName).Descendants()
                .Where(element => element.Name == Presentation + "Button" || element.Name == Presentation + "TextBox")
                .ToArray();

            Assert.NotEmpty(interactiveControls);
            Assert.All(interactiveControls, element =>
                Assert.False(string.IsNullOrWhiteSpace((string?)element.Attribute(Automation + "AutomationProperties.AutomationId")),
                    $"Missing AutomationId on {element.Name.LocalName} in {fileName}."));
        }
    }

    [Fact]
    public void Every_view_static_resource_resolves_from_the_shared_or_local_design_system()
    {
        var resourceFiles = Directory.GetFiles(Path.Combine(ProductAssemblyFixture.RepositoryRoot, "KioskProject", "Resources"), "*.xaml");
        var documents = resourceFiles.Select(path => XDocument.Load(path)).Concat(RequiredAutomationIds.Keys.Select(Load)).ToArray();
        var declaredKeys = documents.SelectMany(document => document.Descendants())
            .Select(element => (string?)element.Attribute(Xaml + "Key"))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        var viewReferences = RequiredAutomationIds.Keys.Select(Source)
            .SelectMany(source => Regex.Matches(source, @"\{StaticResource\s+([^}\s,]+)").Select(match => match.Groups[1].Value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(viewReferences.Where(reference => !declaredKeys.Contains(reference)));
    }

    [Fact]
    public void Views_use_reusable_styles_safe_wrapping_and_scroll_owned_item_bodies()
    {
        var documents = RequiredAutomationIds.Keys.Select(Load).ToArray();
        var source = string.Join('\n', RequiredAutomationIds.Keys.Select(Source));

        Assert.DoesNotMatch(@"#[0-9A-Fa-f]{6,8}", source);
        Assert.DoesNotMatch(@"(?:[A-Za-z]:\\|/Users/|/home/)", source);
        Assert.DoesNotContain("Frame", source, StringComparison.Ordinal);
        Assert.DoesNotContain("NavigationService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Navigate(", source, StringComparison.Ordinal);

        Assert.All(documents, document =>
        {
            Assert.Contains(document.Descendants(Presentation + "TextBlock"), element =>
                (string?)element.Attribute("TextWrapping") == "Wrap" ||
                ((string?)element.Attribute("Style"))?.Contains("KioskText", StringComparison.Ordinal) == true);
            Assert.Contains(document.Descendants(Presentation + "Button"), element =>
                ((string?)element.Attribute("Style"))?.Contains("Kiosk", StringComparison.Ordinal) == true);
        });

        Assert.Empty(Load("MenuView.xaml").Descendants(Presentation + "ScrollViewer"));
        Assert.Contains(Load("CartView.xaml").Descendants(Presentation + "ScrollViewer"), element =>
            (string?)element.Attribute("VerticalScrollBarVisibility") == "Auto");
    }

    private static XDocument Load(string fileName) => XDocument.Load(Path.Combine(ViewsRoot, fileName), LoadOptions.SetLineInfo);

    private static string Source(string fileName) => File.ReadAllText(Path.Combine(ViewsRoot, fileName));

    private static void AssertBindingPaths(string source, params string[] paths)
    {
        foreach (var path in paths)
        {
            Assert.Matches($@"{{Binding\s+(?:Path=)?{Regex.Escape(path)}(?:[,}}])", source);
        }
    }
}
