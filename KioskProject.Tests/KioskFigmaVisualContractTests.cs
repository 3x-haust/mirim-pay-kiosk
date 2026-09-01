using System.Collections.ObjectModel;
using System.Xml.Linq;

namespace KioskProject.Tests;

public sealed class KioskFigmaVisualContractTests
{
    private static readonly XNamespace Automation = "clr-namespace:System.Windows.Automation;assembly=PresentationCore";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly string Root = Path.Combine(ProductAssemblyFixture.RepositoryRoot, "KioskProject");
    private static readonly string Views = Path.Combine(Root, "Views");
    private static readonly string Foundations = Path.Combine(Root, "Resources", "KioskFoundations.xaml");
    private static readonly ReadOnlyDictionary<string, string> Expected = new(new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["KioskLogicalWidth"] = "1080", ["KioskLogicalHeight"] = "1920", ["KioskPrimaryColor"] = "#FF249D57",
        ["KioskTypeBrand"] = "100", ["KioskHeroActionHeight"] = "131", ["KioskPillRadius"] = "86",
        ["KioskBorderOutline"] = "3", ["KioskStartActionType"] = "45", ["KioskStartActionWeight"] = "SemiBold",
        ["OrderButtonX"] = "384", ["OrderButtonY"] = "1566", ["CartListWidth"] = "1015", ["CartItemHeight"] = "164",
        ["CartItemGap"] = "0,0,0,26", ["KioskCardRadius"] = "46", ["KioskCardBorderThickness"] = "3", ["KioskTypeBodyLarge"] = "36",
        ["KioskTypeAction"] = "40", ["CartQuantityVisualSize"] = "50", ["KioskTouchTargetMin"] = "96", ["CartFooterHeight"] = "443",
        ["CartFooterInset"] = "85,43,85,0", ["KioskTypeTitle"] = "48", ["KioskTotalLabelColor"] = "#FF008C0E",
        ["CartPaymentButtonWidth"] = "304", ["KioskActionHeight"] = "108", ["PaymentCardPayX"] = "66",
        ["PaymentPayCardWidth"] = "450", ["PaymentCardFaceX"] = "585", ["PaymentFaceCardWidth"] = "430",
        ["KioskPaymentCardHeight"] = "409", ["PaymentPayIconWidth"] = "110", ["PaymentPayIconHeight"] = "110",
        ["PaymentFaceIconWidth"] = "110", ["PaymentFaceIconHeight"] = "115", ["KioskTextSecondaryColor"] = "#FF808080",
        ["PaymentFooterInset"] = "85,43,85,0", ["PaymentFooterButtonWidth"] = "489", ["KioskSecondaryActionHeight"] = "118",
        ["KioskSuccessCardX"] = "87", ["KioskSuccessCardY"] = "100", ["KioskSuccessCardWidth"] = "906", ["KioskSuccessCardHeight"] = "1718.2609", ["KioskSuccessCardRadius"] = "59",
        ["PaymentSuccessIconX"] = "313", ["PaymentSuccessIconY"] = "300", ["PaymentSuccessIconWidth"] = "280", ["PaymentSuccessIconHeight"] = "278.2609",
        ["PaymentSuccessMessageX"] = "163", ["PaymentSuccessMessageY"] = "728.2609", ["PaymentSuccessMessageWidth"] = "580",
        ["PaymentSuccessMessageHeight"] = "132", ["PaymentSuccessMessageType"] = "50", ["CompleteButtonX"] = "245.5", ["CompleteButtonY"] = "1360.2609", ["CompleteButtonWidth"] = "415"
    });

    [Fact]
    public void Menu_matches_Figma_geometry_contract()
    {
        var xaml = Load("MenuView.xaml");
        AssertResource("KioskLogicalWidth"); AssertResource("KioskLogicalHeight"); AssertResource("KioskPrimaryColor");
        var logo = xaml.Descendants().Single(e => (string?)e.Attribute("Text") == "MIRIM\nPAY");
        Assert.Equal("162,199,0,0", logo.Attribute("Margin")?.Value);
        var order = ById(xaml, "OrderButton");
        AssertResource("KioskHeroActionHeight"); Assert.Equal("418", order.Attribute("Width")?.Value);
        Assert.Equal("{StaticResource OrderButtonX}", order.Attribute("Canvas.Left")?.Value);
        Assert.Equal("{StaticResource OrderButtonY}", order.Attribute("Canvas.Top")?.Value);
        Assert.Equal("{StaticResource KioskPillRadius}", order.Attribute("Tag")?.Value);
        Assert.Equal("{StaticResource KioskBorderOutline}", order.Attribute("BorderThickness")?.Value);
        Assert.Equal("{StaticResource KioskStartActionType}", order.Attribute("FontSize")?.Value);
        Assert.Equal("{StaticResource KioskStartActionWeight}", order.Attribute("FontWeight")?.Value);
        Assert.Equal("주문하기", order.Attribute("Content")?.Value);
        var lines = xaml.Descendants().Where(e => e.Name.LocalName == "Line").Select(e => string.Join(',', new[] { "X1", "Y1", "X2", "Y2" }.Select(n => (string?)e.Attribute(n) ?? "<missing>"))).ToArray().Order(StringComparer.Ordinal);
        Assert.Equal(new[] { "0,1408,101,1408", "0,142,1125,142", "0,914,100,914", "101,0,101,1920" }, lines);
    }

    [Fact]
    public void Cart_view_owns_non_rendering_scanner_controls()
    {
        var cart = Load("CartView.xaml");
        var barcodeInput = ById(cart, "BarcodeInput");
        var barcodeAddButton = ById(cart, "BarcodeAddButton");

        Assert.Equal("{Binding BarcodeInput, UpdateSourceTrigger=PropertyChanged}", barcodeInput.Attribute("Text")?.Value);
        Assert.Equal("{Binding ScanBarcodeCommand}", barcodeAddButton.Attribute("Command")?.Value);
        Assert.Equal("1", barcodeInput.Attribute("Width")?.Value);
        Assert.Equal("1", barcodeInput.Attribute("Height")?.Value);
        Assert.Equal("0", barcodeInput.Attribute("Opacity")?.Value);
        Assert.Equal("1", barcodeAddButton.Attribute("Width")?.Value);
        Assert.Equal("1", barcodeAddButton.Attribute("Height")?.Value);
        Assert.Equal("0", barcodeAddButton.Attribute("MinWidth")?.Value);
        Assert.Equal("0", barcodeAddButton.Attribute("MinHeight")?.Value);
        Assert.Equal("0", barcodeAddButton.Attribute("Opacity")?.Value);
    }

    [Fact]
    public void Cart_clear_button_matches_Figma_label_contract()
    {
        var clearButton = ById(Load("CartView.xaml"), "ClearCartButton");

        Assert.Equal("상품 전체 삭제", clearButton.Attribute("Content")?.Value);
        Assert.Equal("230", clearButton.Attribute("Width")?.Value);
        Assert.Equal("96", clearButton.Attribute("Height")?.Value);
        Assert.Equal("{StaticResource KioskTypeLabel}", clearButton.Attribute("FontSize")?.Value);
    }

    [Fact]
    public void Cart_matches_Figma_geometry_contract()
    {
        var xaml = Load("CartView.xaml");
        var scroll = xaml.Descendants().Single(e => e.Name.LocalName == "ScrollViewer");
        AssertResource("CartListWidth"); Assert.Equal("1022", scroll.Attribute("Width")?.Value);
        var item = xaml.Descendants().Single(e => e.Name.LocalName == "DataTemplate").Descendants().First(e => e.Name.LocalName == "Grid");
        Assert.Equal("{StaticResource CartItemHeight}", item.Attribute("Height")?.Value); Assert.Equal("{StaticResource CartItemGap}", item.Attribute("Margin")?.Value);
        var card = item.Descendants().First(e => e.Name.LocalName == "Border"); Assert.Equal("{StaticResource KioskCardRadius}", card.Attribute("Tag")?.Value);
        var quantity = item.Descendants().Single(e => (string?)e.Attribute("Text") == "{Binding Quantity}");
        Assert.Equal("{StaticResource CartQuantityVisualSize}", quantity.Attribute("MinWidth")?.Value);
        var footer = xaml.Descendants().Single(e => (string?)e.Attribute(Automation + "AutomationProperties.AutomationId") == "CartViewRoot").Elements().Single(e => e.Name.LocalName == "Border" && e.Attribute("Grid.Row")?.Value == "3");
        Assert.Equal("{StaticResource CartFooterInset}", footer.Attribute("Padding")?.Value);
        var button = ById(xaml, "PaymentButton"); Assert.Equal("{StaticResource CartPaymentButtonWidth}", button.Attribute("Width")?.Value);
        Assert.Equal("주문개수", xaml.Descendants().Single(e => (string?)e.Attribute("Text") == "주문개수").Attribute("Text")?.Value);
        AssertResource("CartListWidth"); AssertResource("CartItemHeight"); AssertResource("CartItemGap"); AssertResource("CartQuantityVisualSize"); AssertResource("CartFooterInset"); AssertResource("CartPaymentButtonWidth");
    }

    [Fact]
    public void Payment_matches_Figma_geometry_contract()
    {
        var xaml = Load("PaymentView.xaml");
        var pay = ById(xaml, "PayPaymentButton"); var face = ById(xaml, "FacePaymentButton");
        Assert.Equal("{StaticResource PaymentCardPayX}", pay.Attribute("Canvas.Left")?.Value); Assert.Equal("{StaticResource PaymentPayCardWidth}", pay.Attribute("Width")?.Value);
        Assert.Equal("{StaticResource PaymentCardFaceX}", face.Attribute("Canvas.Left")?.Value); Assert.Equal("{StaticResource PaymentFaceCardWidth}", face.Attribute("Width")?.Value);
        Assert.Equal("{StaticResource KioskPaymentCardHeight}", pay.Attribute("Height")?.Value);
        Assert.Equal("{StaticResource PaymentPayIconWidth}", pay.Descendants().Single(e => e.Name.LocalName == "Image").Attribute("Width")?.Value);
        Assert.Equal("{StaticResource PaymentFaceIconHeight}", face.Descendants().Single(e => e.Name.LocalName == "Image").Attribute("Height")?.Value);
        var footer = xaml.Descendants().Single(e => e.Name.LocalName == "Border" && e.Attribute("Grid.Row")?.Value == "2");
        Assert.Equal("{StaticResource PaymentFooterInset}", footer.Attribute("Padding")?.Value);
        Assert.Equal("{StaticResource PaymentFooterButtonWidth}", ById(xaml, "BackButton").Attribute("Width")?.Value);
        Assert.NotNull(ById(xaml, "NextButton").Attribute("Command"));
        AssertResource("PaymentCardPayX"); AssertResource("PaymentPayCardWidth"); AssertResource("PaymentCardFaceX"); AssertResource("PaymentFaceCardWidth"); AssertResource("PaymentPayIconWidth"); AssertResource("PaymentFaceIconHeight"); AssertResource("PaymentFooterInset"); AssertResource("PaymentFooterButtonWidth");
    }

    [Fact]
    public void Success_matches_Figma_geometry_contract()
    {
        var xaml = Load("PaymentView.xaml"); AssertResource("KioskSuccessCardX"); AssertResource("KioskSuccessCardY"); AssertResource("KioskSuccessCardWidth"); AssertResource("KioskSuccessCardHeight"); AssertResource("KioskSuccessCardRadius"); AssertResource("PaymentSuccessIconX"); AssertResource("PaymentSuccessIconY"); AssertResource("PaymentSuccessIconWidth"); AssertResource("PaymentSuccessIconHeight"); AssertResource("PaymentSuccessMessageX"); AssertResource("PaymentSuccessMessageY"); AssertResource("PaymentSuccessMessageWidth"); AssertResource("PaymentSuccessMessageHeight"); AssertResource("CompleteButtonX"); AssertResource("CompleteButtonY"); AssertResource("CompleteButtonWidth");
        var card = xaml.Descendants().Single(e => e.Name.LocalName == "Border" && e.Attribute("Style")?.Value == "{StaticResource KioskSuccessCardStyle}");
        Assert.Equal("{StaticResource KioskSuccessCardX}", card.Attribute("Canvas.Left")?.Value); Assert.Equal("{StaticResource KioskSuccessCardY}", card.Attribute("Canvas.Top")?.Value);
        Assert.Equal("{StaticResource KioskSuccessCardWidth}", card.Attribute("Width")?.Value); Assert.Equal("{StaticResource KioskSuccessCardHeight}", card.Attribute("Height")?.Value);
        Assert.Equal("{StaticResource KioskSuccessCardRadius}", card.Attribute("Tag")?.Value);
        var icon = card.Descendants().Single(e => e.Name.LocalName == "Image"); Assert.Equal("{StaticResource PaymentSuccessIconX}", icon.Attribute("Canvas.Left")?.Value); Assert.Equal("{StaticResource PaymentSuccessIconY}", icon.Attribute("Canvas.Top")?.Value);
        Assert.Equal("{StaticResource PaymentSuccessIconWidth}", icon.Attribute("Width")?.Value); Assert.Equal("{StaticResource PaymentSuccessIconHeight}", icon.Attribute("Height")?.Value);
        var message = card.Descendants().Single(e => (string?)e.Attribute("Text") == "결제가 성공적으로\n완료되었습니다!");
        Assert.Equal("{StaticResource PaymentSuccessMessageX}", message.Attribute("Canvas.Left")?.Value); Assert.Equal("{StaticResource PaymentSuccessMessageY}", message.Attribute("Canvas.Top")?.Value);
        Assert.Equal("{StaticResource PaymentSuccessMessageWidth}", message.Attribute("Width")?.Value); Assert.Equal("{StaticResource PaymentSuccessMessageHeight}", message.Attribute("Height")?.Value);
        Assert.Equal("{StaticResource PaymentSuccessMessageType}", message.Attribute("FontSize")?.Value);
        var complete = ById(xaml, "CompleteButton"); Assert.Equal("{StaticResource CompleteButtonX}", complete.Attribute("Canvas.Left")?.Value); Assert.Equal("{StaticResource CompleteButtonY}", complete.Attribute("Canvas.Top")?.Value);
        Assert.Equal("{StaticResource CompleteButtonWidth}", complete.Attribute("Width")?.Value); Assert.NotNull(complete.Attribute("Command"));
    }

    [Fact]
    public void Success_brand_uses_bundled_Inter_Bold_resource()
    {
        var fontPath = Path.Combine(Root, "Fonts", "Inter-Bold.otf");
        var fontBytes = File.ReadAllBytes(fontPath);
        Assert.True(fontBytes.Length > 0);
        Assert.Equal("OTTO", System.Text.Encoding.ASCII.GetString(fontBytes, 0, 4));

        var project = File.ReadAllText(Path.Combine(Root, "KioskProject.csproj"));
        Assert.Contains("Fonts\\Inter-Bold.otf", project, StringComparison.Ordinal);

        var foundations = XDocument.Load(Foundations);
        var font = foundations.Descendants().Single(e => (string?)e.Attribute(Xaml + "Key") == "KioskInterFontFamily");
        Assert.Equal("pack://application:,,,/KioskProject;component/Fonts/#Inter", font.Value.Trim());

        var button = XDocument.Load(Path.Combine(Views, "PaymentView.xaml")).Descendants()
            .Single(e => (string?)e.Attribute(Automation + "AutomationProperties.AutomationId") == "CompleteButton");
        Assert.Equal("{StaticResource SuccessAcknowledgeButtonStyle}", button.Attribute("Style")?.Value);
        var style = button.Document!.Descendants().Single(e => (string?)e.Attribute(Xaml + "Key") == "SuccessAcknowledgeButtonStyle");
        Assert.Equal("{StaticResource KioskInterFontFamily}", style.Descendants().Single(e => e.Name.LocalName == "Setter" && (string?)e.Attribute("Property") == "FontFamily").Attribute("Value")?.Value);
    }

    private static XElement ById(XDocument document, string id) => document.Descendants().Single(e => (string?)e.Attribute(Automation + "AutomationProperties.AutomationId") == id);
    private static XDocument Load(string file) => XDocument.Load(Path.Combine(Views, file));
    private static void AssertResource(string key) => Assert.Equal(Expected[key], XDocument.Load(Foundations).Descendants().SingleOrDefault(e => (string?)e.Attribute(Xaml + "Key") == key)?.Value.Trim() ?? "<missing>");
}
