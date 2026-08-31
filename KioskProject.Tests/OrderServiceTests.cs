using System.Collections;
using System.Reflection;

namespace KioskProject.Tests;

public sealed class OrderServiceTests
{
    private static readonly DateTime FixedTime = new(2026, 8, 25, 4, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void SaveOrder_uses_injected_identity_and_clock_when_cart_is_consistent()
    {
        // Given
        var recorder = CreateRecorder();
        var service = CreateService(recorder, "order-fixed", FixedTime);
        var item = OrderTestFixture.CreateItem(OrderTestFixture.CreateMenu(), 1);
        var items = OrderTestFixture.CreateItems(item);

        // When
        var result = OrderTestFixture.Invoke(service, "SaveOrder", items, 2500, "Pay");

        // Then
        Assert.Equal(1, recorder.SaveCount);
        Assert.Same(result, recorder.SavedOrder);
        Assert.Equal("order-fixed", OrderTestFixture.Read<string>(result, "Id"));
        Assert.Equal(FixedTime, OrderTestFixture.Read<DateTime>(result, "OrderedAt"));
        Assert.Equal(2500, OrderTestFixture.Read<int>(result, "TotalPrice"));
        Assert.Equal("Pay", OrderTestFixture.Read<string>(result, "PaymentMethod"));
    }

    [Fact]
    public void Saved_order_remains_unchanged_when_source_cart_and_menu_are_mutated()
    {
        // Given
        var recorder = CreateRecorder();
        var service = CreateService(recorder, "snapshot", FixedTime);
        var menu = OrderTestFixture.CreateMenu();
        var item = OrderTestFixture.CreateItem(menu, 1);
        OrderTestFixture.Invoke(service, "SaveOrder", OrderTestFixture.CreateItems(item), 2500, "Pay");
        var savedOrder = Assert.IsAssignableFrom<object>(recorder.SavedOrder);

        // When
        OrderTestFixture.Write(menu, "Id", 99);
        OrderTestFixture.Write(menu, "Name", "Changed");
        OrderTestFixture.Write(menu, "Price", 9000);
        OrderTestFixture.Write(menu, "Category", "Changed");
        OrderTestFixture.Write(menu, "ImagePath", "changed.png");
        OrderTestFixture.Write(menu, "Stock", 0);
        OrderTestFixture.Write(item, "Quantity", 2);

        // Then
        var savedItem = Assert.Single(OrderTestFixture.ReadItems(savedOrder));
        var savedMenu = OrderTestFixture.Read<object>(savedItem, "Menu");
        Assert.NotSame(item, savedItem);
        Assert.NotSame(menu, savedMenu);
        Assert.Equal(1, OrderTestFixture.Read<int>(savedMenu, "Id"));
        Assert.Equal("Coffee", OrderTestFixture.Read<string>(savedMenu, "Name"));
        Assert.Equal(2500, OrderTestFixture.Read<int>(savedMenu, "Price"));
        Assert.Equal("Coffee", OrderTestFixture.Read<string>(savedMenu, "Category"));
        Assert.Equal("Images/coffee.png", OrderTestFixture.Read<string>(savedMenu, "ImagePath"));
        Assert.Equal(20, OrderTestFixture.Read<int>(savedMenu, "Stock"));
        Assert.Equal(1, OrderTestFixture.Read<int>(savedItem, "Quantity"));
        Assert.Equal(2500, OrderTestFixture.Read<int>(savedOrder, "TotalPrice"));
    }

    [Fact]
    public void SaveOrder_rejects_null_items_before_data_service_call()
    {
        AssertRejected(null, 0, "Pay");
    }

    [Fact]
    public void SaveOrder_rejects_empty_items_before_data_service_call()
    {
        AssertRejected(OrderTestFixture.CreateItems(), 0, "Pay");
    }

    [Fact]
    public void SaveOrder_rejects_null_cart_item_before_data_service_call()
    {
        AssertRejected(OrderTestFixture.CreateItems((object?)null), 0, "Pay");
    }

    [Fact]
    public void SaveOrder_rejects_inconsistent_total_before_data_service_call()
    {
        var item = OrderTestFixture.CreateItem(OrderTestFixture.CreateMenu(), 1);
        AssertRejected(OrderTestFixture.CreateItems(item), 5000, "Pay");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SaveOrder_rejects_blank_payment_method_before_data_service_call(string paymentMethod)
    {
        var item = OrderTestFixture.CreateItem(OrderTestFixture.CreateMenu(), 1);
        AssertRejected(OrderTestFixture.CreateItems(item), 2500, paymentMethod);
    }

    private static void AssertRejected(IList? items, int totalPrice, string paymentMethod)
    {
        // Given
        var recorder = CreateRecorder();
        var service = CreateService(recorder, "unused", FixedTime);

        // When
        var exception = OrderTestFixture.InvokeException(service, "SaveOrder", items, totalPrice, paymentMethod);

        // Then
        Assert.IsType(items is null ? typeof(ArgumentNullException) : typeof(ArgumentException), exception);
        Assert.Equal(0, recorder.SaveCount);
        Assert.Null(recorder.SavedOrder);
    }

    private static RecordingDataServiceProxy CreateRecorder()
    {
        var interfaceType = OrderTestFixture.RequireType("KioskProject.Services.IDataService");
        var proxy = DispatchProxy.Create(interfaceType, typeof(RecordingDataServiceProxy));
        return Assert.IsAssignableFrom<RecordingDataServiceProxy>(proxy);
    }

    private static object CreateService(
        object dataService,
        string id,
        DateTime time)
    {
        var serviceType = OrderTestFixture.RequireType("KioskProject.Services.OrderService");
        var constructor = Assert.Single(serviceType.GetConstructors(
            BindingFlags.Instance | BindingFlags.NonPublic).Where(candidate =>
                candidate.GetParameters().Select(parameter => parameter.ParameterType)
                    .SequenceEqual(new[]
                    {
                        OrderTestFixture.RequireType("KioskProject.Services.IDataService"),
                        typeof(Func<string>),
                        typeof(Func<DateTime>)
                    })));
        return constructor.Invoke(new object[]
        {
            dataService,
            new Func<string>(() => id),
            new Func<DateTime>(() => time)
        });
    }
}

public class RecordingDataServiceProxy : DispatchProxy
{
    internal int SaveCount { get; private set; }

    internal object? SavedOrder { get; private set; }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? arguments)
    {
        Assert.NotNull(targetMethod);
        if (targetMethod.Name == "SaveOrder")
        {
            SaveCount++;
            SavedOrder = Assert.Single(Assert.IsAssignableFrom<object?[]>(arguments));
            return null;
        }

        return Activator.CreateInstance(targetMethod.ReturnType);
    }
}
