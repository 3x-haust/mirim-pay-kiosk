using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace KioskProject.Tests;

public sealed class KioskShellCompositionTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly string ProductRoot = Path.Combine(ProductAssemblyFixture.RepositoryRoot, "KioskProject");

    [Fact]
    public void Composition_uses_one_data_service_and_one_shared_cart_when_created()
    {
        // Given
        using var menu = MenuFile.Create(CanonicalMenu);
        var dataService = CreateDataService(menu.Path);

        // When
        var viewModel = CreateComposition(dataService);

        // Then
        var cart = Read<object>(viewModel, "Cart");
        var menuViewModel = Read<object>(viewModel, "Menu");
        var orderService = Read<object>(viewModel, "OrderService");
        Assert.Same(dataService, Read<object>(viewModel, "DataService"));
        Assert.Same(cart, Read<object>(menuViewModel, "Cart"));
        Assert.Same(dataService, PrivateField(orderService, "_dataService"));
        Assert.Equal("Menu", Read<object>(viewModel, "CurrentView").ToString());
    }

    [Fact]
    public void Composition_stays_on_menu_and_disables_ordering_when_catalog_is_malformed()
    {
        // Given
        using var menu = MenuFile.Create("[");
        var dataService = CreateDataService(menu.Path);

        // When
        var viewModel = CreateComposition(dataService);

        // Then
        var menuViewModel = Read<object>(viewModel, "Menu");
        Assert.Equal("Menu", Read<object>(viewModel, "CurrentView").ToString());
        Assert.True(Read<bool>(menuViewModel, "HasLoadError"));
        Assert.False(Read<bool>(menuViewModel, "CanPurchase"));
    }

    [Fact]
    public void Application_startup_creates_one_window_and_main_window_sets_data_context_once()
    {
        // Given
        var appXaml = Source("App.xaml");
        var appCode = Source("App.xaml.cs");
        var windowCode = Source("MainWindow.xaml.cs");

        // When / Then
        Assert.DoesNotContain("StartupUri", appXaml, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(appCode, @"new\s+JsonDataService\s*\(").Cast<Match>());
        Assert.Single(Regex.Matches(appCode, @"KioskComposition\.Create\s*\(").Cast<Match>());
        Assert.Single(Regex.Matches(appCode, @"new\s+MainWindow\s*\(").Cast<Match>());
        Assert.Single(Regex.Matches(windowCode, @"\bDataContext\s*=").Cast<Match>());
        Assert.Contains("MainWindow(MainViewModel viewModel)", windowCode, StringComparison.Ordinal);
        Assert.DoesNotContain("DataContext=", Source("MainWindow.xaml"), StringComparison.Ordinal);
    }

    [Fact]
    public void Current_view_maps_exactly_once_to_each_product_view_template()
    {
        // Given
        var document = XDocument.Load(Path.Combine(ProductRoot, "MainWindow.xaml"));
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Menu"] = "MenuView",
            ["Cart"] = "CartView",
            ["Payment"] = "PaymentView"
        };

        // When
        var templates = document.Descendants(Presentation + "DataTemplate").ToDictionary(
            template => (string)template.Attribute(Xaml + "Key")!,
            template => template.Elements().Single().Name.LocalName,
            StringComparer.Ordinal);
        var mappings = document.Descendants(Presentation + "DataTrigger").ToDictionary(
            trigger => ((string)trigger.Attribute("Value")!).Split('.').Last().TrimEnd('}'),
            trigger => ((string)trigger.Elements(Presentation + "Setter").Single().Attribute("Value")!).Trim('{', '}').Split(' ').Last(),
            StringComparer.Ordinal);

        // Then
        Assert.Equal(expected.Keys.Select(key => $"{key}ViewTemplate").Order(), templates.Keys.Order());
        Assert.All(expected, pair => Assert.Equal(pair.Value, templates[$"{pair.Key}ViewTemplate"]));
        Assert.Equal(expected.Keys.Order(), mappings.Keys.Order());
        Assert.All(expected.Keys, key => Assert.Equal($"{key}ViewTemplate", mappings[key]));
        Assert.All(document.Descendants(Presentation + "DataTrigger"), trigger =>
            Assert.Equal("{Binding CurrentView}", (string?)trigger.Attribute("Binding")));
        var host = Assert.Single(document.Descendants(Presentation + "ContentControl"));
        Assert.Equal("{Binding}", (string?)host.Attribute("Content"));
    }

    [Fact]
    public void Window_is_a_uniform_portrait_kiosk_without_secondary_routing()
    {
        // Given
        var document = XDocument.Load(Path.Combine(ProductRoot, "MainWindow.xaml"));
        var window = document.Root!;
        var productSource = string.Join('\n', Directory.GetFiles(ProductRoot, "*", SearchOption.AllDirectories)
            .Where(path => (path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".xaml", StringComparison.Ordinal)) &&
                           !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                           !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(File.ReadAllText));

        // When / Then
        Assert.Equal("1080", (string?)window.Attribute("Width"));
        Assert.Equal("1920", (string?)window.Attribute("Height"));
        Assert.Equal("None", (string?)window.Attribute("WindowStyle"));
        Assert.Equal("NoResize", (string?)window.Attribute("ResizeMode"));
        Assert.Equal("Maximized", (string?)window.Attribute("WindowState"));
        Assert.Equal("True", (string?)window.Attribute("ShowInTaskbar"));
        Assert.DoesNotMatch(@"\bFrame\b|NavigationService|\bNavigate\s*\(", productSource);
        var shellSource = string.Join('\n', new[] { "App.xaml.cs", "MainWindow.xaml", "MainWindow.xaml.cs" }
            .Select(Source)
            .Concat(Directory.GetFiles(Path.Combine(ProductRoot, "Views"), "*.xaml").Select(File.ReadAllText)));
        Assert.DoesNotMatch(@"new\s+(?:Menu|Cart|Payment)ViewModel\s*\(", shellSource);
        Assert.Contains("AppContext.BaseDirectory", Source(Path.Combine("Services", "JsonDataService.cs")), StringComparison.Ordinal);
    }

    private static object CreateComposition(object dataService)
    {
        var composition = Assert.IsAssignableFrom<Type>(ProductAssemblyFixture.Assembly.GetType("KioskProject.KioskComposition"));
        var method = Assert.Single(composition.GetMethods(BindingFlags.Static | BindingFlags.NonPublic), candidate => candidate.Name == "Create");
        return Assert.IsAssignableFrom<object>(method.Invoke(null, new[] { dataService }));
    }

    private static object CreateDataService(string path)
    {
        var type = MainViewModelFixture.RequireType("KioskProject.Services.JsonDataService");
        return Assert.IsAssignableFrom<object>(Activator.CreateInstance(type, path));
    }

    private static T Read<T>(object target, string propertyName) =>
        Assert.IsAssignableFrom<T>(Assert.IsAssignableFrom<PropertyInfo>(target.GetType().GetProperty(propertyName)).GetValue(target));

    private static object PrivateField(object target, string fieldName) =>
        Assert.IsAssignableFrom<object>(Assert.IsAssignableFrom<FieldInfo>(target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)).GetValue(target));

    private static string Source(string relativePath) => File.ReadAllText(Path.Combine(ProductRoot, relativePath));

    private const string CanonicalMenu = "[{\"id\":1,\"name\":\"Coffee\",\"price\":2500,\"category\":\"Coffee\",\"imagePath\":\"Images/americano.png\",\"stock\":20}]";

    private sealed class MenuFile : IDisposable
    {
        private MenuFile(string directory)
        {
            DirectoryPath = directory;
            Path = System.IO.Path.Combine(directory, "menu.json");
        }

        internal string DirectoryPath { get; }
        internal string Path { get; }

        internal static MenuFile Create(string json)
        {
            var fixture = new MenuFile(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "KioskProject.Tests", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(fixture.DirectoryPath);
            File.WriteAllText(fixture.Path, json);
            return fixture;
        }

        public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
    }
}
