using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json;

namespace KioskProject.Tests;

public sealed class PublicContractTests
{
    private static readonly Assembly ProductAssembly = ProductAssemblyFixture.Assembly;

    [Fact]
    public void Public_models_expose_the_fixed_members_when_contract_is_inspected()
    {
        // Given
        var menuType = RequireProductType("KioskProject.Models.MenuItem");
        var cartType = RequireProductType("KioskProject.Models.CartItem");
        var orderType = RequireProductType("KioskProject.Models.Order");

        // When
        var menuProperties = PublicProperties(menuType);
        var cartProperties = PublicProperties(cartType);
        var orderProperties = PublicProperties(orderType);

        // Then
        AssertProperty(menuProperties, new("Id", typeof(int), true));
        AssertProperty(menuProperties, new("Name", typeof(string), true));
        AssertProperty(menuProperties, new("Price", typeof(int), true));
        AssertProperty(menuProperties, new("Category", typeof(string), true));
        AssertProperty(menuProperties, new("ImagePath", typeof(string), true));
        AssertProperty(menuProperties, new("Stock", typeof(int), true));
        Assert.Equal(6, menuProperties.Count);

        AssertProperty(cartProperties, new("Menu", menuType, true));
        AssertProperty(cartProperties, new("Quantity", typeof(int), true));
        AssertProperty(cartProperties, new("Subtotal", typeof(int), false));
        Assert.Equal(3, cartProperties.Count);

        AssertProperty(orderProperties, new("Id", typeof(string), true));
        AssertProperty(orderProperties, new("OrderedAt", typeof(DateTime), true));
        AssertProperty(orderProperties, new("Items", typeof(List<>).MakeGenericType(cartType), true));
        AssertProperty(orderProperties, new("TotalPrice", typeof(int), true));
        AssertProperty(orderProperties, new("PaymentMethod", typeof(string), true));
        Assert.Equal(5, orderProperties.Count);
    }

    [Fact]
    public void Cart_view_model_exposes_the_fixed_cart_api_when_contract_is_inspected()
    {
        // Given
        var menuType = RequireProductType("KioskProject.Models.MenuItem");
        var cartType = RequireProductType("KioskProject.Models.CartItem");
        var viewModelType = RequireProductType("KioskProject.ViewModels.CartViewModel");

        // When
        var properties = PublicProperties(viewModelType);

        // Then
        AssertProperty(properties, new("CartItems", typeof(ObservableCollection<>).MakeGenericType(cartType), false));
        AssertProperty(properties, new("TotalPrice", typeof(int), false));
        AssertMethod(viewModelType, new("AddToCart", typeof(void), new[] { menuType, typeof(int) }));
        AssertMethod(viewModelType, new("RemoveFromCart", typeof(void), new[] { cartType }));
        AssertMethod(viewModelType, new("ChangeQuantity", typeof(void), new[] { cartType, typeof(int) }));
    }

    [Fact]
    public void Data_service_exposes_the_fixed_sync_api_when_contract_is_inspected()
    {
        // Given
        var menuType = RequireProductType("KioskProject.Models.MenuItem");
        var orderType = RequireProductType("KioskProject.Models.Order");
        var serviceType = RequireProductType("KioskProject.Services.IDataService");

        // When
        var methods = serviceType.GetMethods(BindingFlags.Instance | BindingFlags.Public);

        // Then
        Assert.True(serviceType.IsInterface);
        Assert.Equal(2, methods.Length);
        AssertMethod(serviceType, new("LoadMenus", typeof(List<>).MakeGenericType(menuType), Array.Empty<Type>()));
        AssertMethod(serviceType, new("SaveOrder", typeof(void), new[] { orderType }));
    }

    [Fact]
    public void Canonical_data_has_exact_names_and_json_token_kinds_when_parsed()
    {
        // Given
        var repositoryRoot = ProductAssemblyFixture.RepositoryRoot;
        using var menuDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(repositoryRoot, "KioskProject", "Data", "menu.json")));
        using var ordersDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(repositoryRoot, "KioskProject", "Data", "orders.json")));
        var expected = new (string Name, JsonValueKind Kind)[]
        {
            ("id", JsonValueKind.Number),
            ("name", JsonValueKind.String),
            ("price", JsonValueKind.Number),
            ("category", JsonValueKind.String),
            ("imagePath", JsonValueKind.String),
            ("stock", JsonValueKind.Number)
        };

        // When
        var menu = Assert.Single(menuDocument.RootElement.EnumerateArray());
        var actual = menu.EnumerateObject().Select(property => (property.Name, property.Value.ValueKind)).ToArray();

        // Then
        Assert.Equal(expected, actual);
        Assert.True(menu.GetProperty("id").TryGetInt32(out _));
        Assert.True(menu.GetProperty("price").TryGetInt32(out _));
        Assert.True(menu.GetProperty("stock").TryGetInt32(out _));
        Assert.Equal(JsonValueKind.Array, ordersDocument.RootElement.ValueKind);
        Assert.Empty(ordersDocument.RootElement.EnumerateArray());

        var outputPath = Environment.GetEnvironmentVariable("MANUAL_CONTRACT_OUTPUT");
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            var lines = actual.Select(property => $"{property.Name}={property.ValueKind}")
                .Append("orders=Array(empty)")
                .Append("PASS");
            var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            Assert.NotNull(outputDirectory);
            Directory.CreateDirectory(outputDirectory);
            File.WriteAllLines(outputPath, lines);
        }
    }

    private static Type RequireProductType(string fullName) =>
        Assert.IsAssignableFrom<Type>(ProductAssembly.GetType(fullName));

    private static Dictionary<string, PropertyInfo> PublicProperties(Type type) =>
        type.GetProperties(BindingFlags.Instance | BindingFlags.Public).ToDictionary(property => property.Name);

    private static void AssertProperty(
        IReadOnlyDictionary<string, PropertyInfo> properties,
        ExpectedProperty expected)
    {
        var property = properties[expected.Name];
        Assert.Equal(expected.PropertyType, property.PropertyType);
        Assert.NotNull(property.GetMethod);
        Assert.Equal(expected.HasSetter, property.SetMethod is not null);
    }

    private static void AssertMethod(Type type, ExpectedMethod expected)
    {
        var method = Assert.IsAssignableFrom<MethodInfo>(type.GetMethod(expected.Name, expected.ParameterTypes));
        Assert.Equal(expected.ReturnType, method.ReturnType);
    }

    private sealed record ExpectedProperty(string Name, Type PropertyType, bool HasSetter);

    private sealed record ExpectedMethod(string Name, Type ReturnType, Type[] ParameterTypes);
}
