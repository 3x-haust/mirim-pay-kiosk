using System.Reflection;
using System.Text.Json;

namespace KioskProject.Tests;

public sealed class JsonDataServiceLoadMenusTests
{
    private static readonly Assembly ProductAssembly = ProductAssemblyFixture.Assembly;

    [Fact]
    public void LoadMenus_returns_canonical_menu_when_file_is_valid()
    {
        // Given
        var path = Path.Combine(ProductAssemblyFixture.RepositoryRoot, "KioskProject", "Data", "menu.json");

        // When
        var menus = LoadMenus(path);

        // Then
        var menu = Assert.Single(menus);
        Assert.Equal(1, Read<int>(menu, "Id"));
        Assert.Equal("아메리카노", Read<string>(menu, "Name"));
        Assert.Equal(2500, Read<int>(menu, "Price"));
        Assert.Equal("커피", Read<string>(menu, "Category"));
        Assert.Equal("Images/americano.png", Read<string>(menu, "ImagePath"));
        Assert.Equal(20, Read<int>(menu, "Stock"));
    }

    [Fact]
    public void LoadMenus_returns_empty_list_when_root_array_is_empty()
    {
        // Given
        using var fixture = MenuFile.Create("[]");

        // When
        var menus = LoadMenus(fixture.Path);

        // Then
        Assert.Empty(menus);
    }

    [Fact]
    public void LoadMenus_throws_file_not_found_when_file_is_missing()
    {
        // Given
        using var fixture = MenuFile.CreateWithoutFile();

        // When
        var exception = InvokeLoadMenus(fixture.Path);

        // Then
        Assert.IsType<FileNotFoundException>(exception);
    }

    [Theory]
    [InlineData("[")]
    [InlineData("[/*comment*/]")]
    [InlineData("[{},]")]
    public void LoadMenus_throws_invalid_data_with_json_inner_when_json_is_malformed(string json)
    {
        // Given
        using var fixture = MenuFile.Create(json);

        // When
        var exception = InvokeLoadMenus(fixture.Path);

        // Then
        var invalidData = Assert.IsType<InvalidDataException>(exception);
        Assert.IsAssignableFrom<JsonException>(invalidData.InnerException);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("\"menu\"")]
    public void LoadMenus_throws_invalid_data_when_root_is_not_an_array(string json)
    {
        // Given
        using var fixture = MenuFile.Create(json);

        // When
        var exception = InvokeLoadMenus(fixture.Path);

        // Then
        Assert.IsType<InvalidDataException>(exception);
    }

    [Theory]
    [InlineData("{\"name\":\"Coffee\",\"price\":2500,\"category\":\"Coffee\",\"imagePath\":\"Images/a.png\",\"stock\":20}")]
    [InlineData("{\"id\":1,\"name\":\"Coffee\",\"price\":2500,\"category\":\"Coffee\",\"imagePath\":\"Images/a.png\"}")]
    public void LoadMenus_throws_invalid_data_when_required_key_is_missing(string item)
    {
        AssertInvalidItem(item);
    }

    [Fact]
    public void LoadMenus_throws_invalid_data_when_extra_key_exists()
    {
        AssertInvalidItem(ValidItem(extra: ",\"description\":\"Hot\""));
    }

    [Theory]
    [InlineData("Id")]
    [InlineData("Name")]
    [InlineData("ImagePath")]
    [InlineData("STOCK")]
    public void LoadMenus_throws_invalid_data_when_key_casing_differs(string replacement)
    {
        // Given
        var original = replacement.ToLowerInvariant() switch
        {
            "id" => "id",
            "name" => "name",
            "imagepath" => "imagePath",
            "stock" => "stock",
            _ => throw new InvalidOperationException()
        };
        var item = ValidItem().Replace($"\"{original}\"", $"\"{replacement}\"", StringComparison.Ordinal);

        // When / Then
        AssertInvalidItem(item);
    }

    [Theory]
    [InlineData("id", "\"1\"")]
    [InlineData("name", "null")]
    [InlineData("price", "\"2500\"")]
    [InlineData("category", "20")]
    [InlineData("imagePath", "{}")]
    [InlineData("stock", "20.0")]
    public void LoadMenus_throws_invalid_data_when_property_token_is_wrong(string property, string value)
    {
        AssertInvalidItem(ValidItem(overrides: new Dictionary<string, string> { [property] = value }));
    }

    [Theory]
    [InlineData("id", "0")]
    [InlineData("id", "-1")]
    [InlineData("id", "2147483648")]
    [InlineData("price", "-1")]
    [InlineData("stock", "-1")]
    public void LoadMenus_throws_invalid_data_when_number_is_out_of_range(string property, string value)
    {
        AssertInvalidItem(ValidItem(overrides: new Dictionary<string, string> { [property] = value }));
    }

    [Theory]
    [InlineData("name", "\"\"")]
    [InlineData("name", "\"   \"")]
    [InlineData("category", "\"\t\"")]
    [InlineData("imagePath", "\"\"")]
    public void LoadMenus_throws_invalid_data_when_string_is_blank(string property, string value)
    {
        AssertInvalidItem(ValidItem(overrides: new Dictionary<string, string> { [property] = value }));
    }

    [Theory]
    [InlineData("../secret.png")]
    [InlineData("Images/../../secret.png")]
    [InlineData("C:/secret.png")]
    [InlineData("\\\\server\\share\\secret.png")]
    [InlineData("https://example.com/menu.png")]
    [InlineData("file:///secret.png")]
    [InlineData("Assets/menu.png")]
    [InlineData("Images\\products\\a.png")]
    public void LoadMenus_rejects_unsafe_image_path_that_can_escape_packaged_assets(string imagePath)
    {
        // Given
        var item = ValidItem(overrides: new Dictionary<string, string>
        {
            ["imagePath"] = JsonSerializer.Serialize(imagePath)
        });

        // When / Then
        AssertInvalidItem(item);
    }

    [Fact]
    public void LoadMenus_throws_invalid_data_when_image_path_contains_embedded_nul()
    {
        // Given
        var imagePath = "Images/a\0.png";
        var item = ValidItem(overrides: new Dictionary<string, string>
        {
            ["imagePath"] = JsonSerializer.Serialize(imagePath)
        });

        // When / Then
        AssertInvalidItem(item);
    }

    [Fact]
    public void LoadMenus_throws_invalid_data_when_id_is_duplicated()
    {
        // Given
        using var fixture = MenuFile.Create($"[{ValidItem()},{ValidItem()}]");

        // When
        var exception = InvokeLoadMenus(fixture.Path);

        // Then
        Assert.IsType<InvalidDataException>(exception);
    }

    [Fact]
    public void LoadMenus_returns_no_partial_catalog_when_later_item_is_invalid()
    {
        // Given
        var invalidItem = ValidItem(overrides: new Dictionary<string, string> { ["price"] = "\"2500\"" });
        using var fixture = MenuFile.Create($"[{ValidItem()},{invalidItem}]");

        // When
        var exception = InvokeLoadMenus(fixture.Path);

        // Then
        Assert.IsType<InvalidDataException>(exception);
    }

    private static void AssertInvalidItem(string item)
    {
        using var fixture = MenuFile.Create($"[{item}]");
        var exception = InvokeLoadMenus(fixture.Path);
        Assert.IsType<InvalidDataException>(exception);
    }

    private static IReadOnlyList<object> LoadMenus(string path)
    {
        var service = CreateService(path);
        var result = Assert.IsAssignableFrom<System.Collections.IEnumerable>(RequireMethod(service).Invoke(service, null));
        return result.Cast<object>().ToArray();
    }

    private static Exception InvokeLoadMenus(string path)
    {
        var service = CreateService(path);
        var exception = Assert.Throws<TargetInvocationException>(() => RequireMethod(service).Invoke(service, null));
        return Assert.IsAssignableFrom<Exception>(exception.InnerException);
    }

    private static object CreateService(string path)
    {
        var serviceType = Assert.IsAssignableFrom<Type>(ProductAssembly.GetType("KioskProject.Services.JsonDataService"));
        var pathConstructor = serviceType.GetConstructor(new[] { typeof(string) });
        return pathConstructor is null
            ? Assert.IsAssignableFrom<object>(Activator.CreateInstance(serviceType))
            : Assert.IsAssignableFrom<object>(pathConstructor.Invoke(new object[] { path }));
    }

    private static MethodInfo RequireMethod(object service) =>
        Assert.IsAssignableFrom<MethodInfo>(service.GetType().GetMethod("LoadMenus"));

    private static T Read<T>(object value, string propertyName) =>
        Assert.IsType<T>(Assert.IsAssignableFrom<PropertyInfo>(value.GetType().GetProperty(propertyName)).GetValue(value));

    private static string ValidItem(
        IReadOnlyDictionary<string, string>? overrides = null,
        string extra = "")
    {
        string Value(string name, string fallback) =>
            overrides is not null && overrides.TryGetValue(name, out var value) ? value : fallback;

        return $$"""{"id":{{Value("id", "1")}},"name":{{Value("name", "\"Coffee\"")}},"price":{{Value("price", "2500")}},"category":{{Value("category", "\"Coffee\"")}},"imagePath":{{Value("imagePath", "\"Images/a.png\"")}},"stock":{{Value("stock", "20")}}{{extra}}}""";
    }

    private sealed class MenuFile : IDisposable
    {
        private readonly string _directory;

        private MenuFile(string directory)
        {
            _directory = directory;
            Path = System.IO.Path.Combine(directory, "menu.json");
        }

        internal string Path { get; }

        internal static MenuFile Create(string json)
        {
            var fixture = CreateWithoutFile();
            File.WriteAllText(fixture.Path, json);
            return fixture;
        }

        internal static MenuFile CreateWithoutFile()
        {
            var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "KioskProject.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return new MenuFile(directory);
        }

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
