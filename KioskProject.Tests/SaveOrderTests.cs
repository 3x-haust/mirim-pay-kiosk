using System.Text;
using System.Text.Json;

namespace KioskProject.Tests;

public sealed partial class SaveOrderTests
{
    private static readonly DateTime FirstTime = new(2026, 8, 25, 4, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void SaveOrder_creates_array_with_first_order_when_destination_is_missing()
    {
        // Given
        using var directory = OrderDirectory.Create();
        var service = OrderTestFixture.CreateJsonService(directory.Path);
        var order = CreateOrder("first", FirstTime, 1);

        // When
        OrderTestFixture.InvokeVoid(service, "SaveOrder", order);

        // Then
        var saved = Assert.Single(OrderTestFixture.ReadOrderElements(directory.OrdersPath));
        Assert.Equal("first", saved.GetProperty("id").GetString());
        Assert.Equal(2500, saved.GetProperty("totalPrice").GetInt32());
        Assert.Empty(directory.TemporaryFiles());
    }

    [Fact]
    public void SaveOrder_appends_second_order_without_losing_first()
    {
        // Given
        using var directory = OrderDirectory.Create();
        var service = OrderTestFixture.CreateJsonService(directory.Path);
        OrderTestFixture.InvokeVoid(service, "SaveOrder", CreateOrder("first", FirstTime, 1));
        var secondTime = FirstTime.AddMinutes(1);

        // When
        OrderTestFixture.InvokeVoid(service, "SaveOrder", CreateOrder("second", secondTime, 2));

        // Then
        var saved = OrderTestFixture.ReadOrderElements(directory.OrdersPath);
        Assert.Equal(2, saved.Length);
        Assert.Equal("first", saved[0].GetProperty("id").GetString());
        Assert.Equal(2500, saved[0].GetProperty("totalPrice").GetInt32());
        Assert.Equal("second", saved[1].GetProperty("id").GetString());
        Assert.Equal(5000, saved[1].GetProperty("totalPrice").GetInt32());
        Assert.Empty(directory.TemporaryFiles());
    }

    [Theory]
    [InlineData("[")]
    [InlineData("null")]
    [InlineData("{}")]
    public void SaveOrder_preserves_destination_bytes_when_existing_content_is_invalid(string content)
    {
        // Given
        using var directory = OrderDirectory.Create();
        var originalBytes = Encoding.UTF8.GetBytes(content);
        File.WriteAllBytes(directory.OrdersPath, originalBytes);
        var service = OrderTestFixture.CreateJsonService(directory.Path);

        // When
        var exception = OrderTestFixture.InvokeException(
            service,
            "SaveOrder",
            CreateOrder("new", FirstTime, 1));

        // Then
        Assert.IsType<InvalidDataException>(exception);
        Assert.Equal(originalBytes, File.ReadAllBytes(directory.OrdersPath));
        Assert.Empty(directory.TemporaryFiles());
    }

    [Fact]
    public void SaveOrder_semantically_reloads_order_and_nested_values_after_commit()
    {
        // Given
        using var directory = OrderDirectory.Create();
        var service = OrderTestFixture.CreateJsonService(directory.Path);
        var order = CreateOrder("semantic", FirstTime, 2);

        // When
        OrderTestFixture.InvokeVoid(service, "SaveOrder", order);

        // Then
        var listType = typeof(List<>).MakeGenericType(OrderTestFixture.RequireType("KioskProject.Models.Order"));
        var reloaded = Assert.IsAssignableFrom<System.Collections.IEnumerable>(JsonSerializer.Deserialize(
            File.ReadAllText(directory.OrdersPath),
            listType,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = false
            }));
        var savedOrder = Assert.Single(reloaded.Cast<object>());
        var savedItem = Assert.Single(OrderTestFixture.ReadItems(savedOrder));
        var savedMenu = OrderTestFixture.Read<object>(savedItem, "Menu");
        Assert.Equal("semantic", OrderTestFixture.Read<string>(savedOrder, "Id"));
        Assert.Equal(FirstTime, OrderTestFixture.Read<DateTime>(savedOrder, "OrderedAt"));
        Assert.Equal(5000, OrderTestFixture.Read<int>(savedOrder, "TotalPrice"));
        Assert.Equal("Pay", OrderTestFixture.Read<string>(savedOrder, "PaymentMethod"));
        Assert.Equal(2, OrderTestFixture.Read<int>(savedItem, "Quantity"));
        Assert.Equal(1, OrderTestFixture.Read<int>(savedMenu, "Id"));
        Assert.Equal("Coffee", OrderTestFixture.Read<string>(savedMenu, "Name"));
        Assert.Equal(2500, OrderTestFixture.Read<int>(savedMenu, "Price"));
        Assert.Equal(20, OrderTestFixture.Read<int>(savedMenu, "Stock"));
    }

    private static object CreateOrder(string id, DateTime time, int quantity)
    {
        var item = OrderTestFixture.CreateItem(OrderTestFixture.CreateMenu(), quantity);
        return OrderTestFixture.CreateOrder(id, time, OrderTestFixture.CreateItems(item));
    }
}
