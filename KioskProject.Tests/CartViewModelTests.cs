using System.Collections;
using System.ComponentModel;
using System.Reflection;

namespace KioskProject.Tests;

public sealed partial class CartViewModelTests
{
    private static readonly Assembly ProductAssembly = ProductAssemblyFixture.Assembly;

    [Fact]
    public void AddToCart_adds_requested_quantity_when_stock_is_available()
    {
        // Given
        var cart = CreateCart();
        var menu = CreateMenu(id: 1, name: "Coffee", price: 2500, stock: 20);
        var notifications = Observe(cart);

        // When
        Invoke(cart, "AddToCart", menu, 2);

        // Then
        var item = Assert.Single(Items(cart));
        Assert.Equal(2, Read<int>(item, "Quantity"));
        Assert.Equal(5000, Read<int>(cart, "TotalPrice"));
        Assert.Equal(new[] { "TotalPrice", "TotalQuantity" }, notifications);
    }

    [Fact]
    public void AddToCart_merges_quantities_when_menu_ids_match()
    {
        // Given
        var cart = CreateCart();
        Invoke(cart, "AddToCart", CreateMenu(1, "Coffee", 2500, 20), 2);
        var notifications = Observe(cart);

        // When
        Invoke(cart, "AddToCart", CreateMenu(1, "Renamed", 9999, 20), 3);

        // Then
        var item = Assert.Single(Items(cart));
        Assert.Equal(5, Read<int>(item, "Quantity"));
        Assert.Equal(12500, Read<int>(cart, "TotalPrice"));
        Assert.Equal(new[] { "TotalQuantity", "TotalPrice" }, notifications);
    }

    [Fact]
    public void AddToCart_keeps_separate_lines_when_only_names_match()
    {
        // Given
        var cart = CreateCart();
        Invoke(cart, "AddToCart", CreateMenu(1, "Coffee", 2500, 20), 1);

        // When
        Invoke(cart, "AddToCart", CreateMenu(2, "Coffee", 3000, 20), 1);

        // Then
        Assert.Equal(new[] { 1, 2 }, Items(cart).Select(item => Read<int>(Read<object>(item, "Menu"), "Id")));
        Assert.Equal(5500, Read<int>(cart, "TotalPrice"));
    }

    [Theory]
    [InlineData(1, 2, 3, 7500)]
    [InlineData(3, -2, 1, 2500)]
    public void ChangeQuantity_applies_delta_when_target_is_in_stock(
        int initialQuantity,
        int delta,
        int expectedQuantity,
        int expectedTotal)
    {
        // Given
        var cart = CreateCart();
        Invoke(cart, "AddToCart", CreateMenu(1, "Coffee", 2500, 20), initialQuantity);
        var item = Assert.Single(Items(cart));
        var notifications = Observe(cart);

        // When
        Invoke(cart, "ChangeQuantity", item, delta);

        // Then
        Assert.Equal(expectedQuantity, Read<int>(item, "Quantity"));
        Assert.Equal(expectedTotal, Read<int>(cart, "TotalPrice"));
        Assert.Equal(new[] { "TotalQuantity", "TotalPrice" }, notifications);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-2)]
    [InlineData(int.MinValue)]
    public void ChangeQuantity_removes_line_when_target_is_not_positive(int delta)
    {
        // Given
        var cart = CreateCart();
        var collection = Read<object>(cart, "CartItems");
        Invoke(cart, "AddToCart", CreateMenu(1, "Coffee", 2500, 20), 1);
        var item = Assert.Single(Items(cart));
        var notifications = Observe(cart);

        // When
        Invoke(cart, "ChangeQuantity", item, delta);

        // Then
        Assert.Empty(Items(cart));
        Assert.Same(collection, Read<object>(cart, "CartItems"));
        Assert.Equal(0, Read<int>(cart, "TotalPrice"));
        Assert.Equal(new[] { "TotalPrice", "TotalQuantity" }, notifications);
    }

    [Fact]
    public void RemoveFromCart_removes_member_and_notifies_total_once()
    {
        // Given
        var cart = CreateCart();
        Invoke(cart, "AddToCart", CreateMenu(1, "Coffee", 2500, 20), 2);
        var item = Assert.Single(Items(cart));
        var notifications = Observe(cart);

        // When
        Invoke(cart, "RemoveFromCart", item);

        // Then
        Assert.Empty(Items(cart));
        Assert.Equal(0, Read<int>(cart, "TotalPrice"));
        Assert.Equal(new[] { "TotalPrice", "TotalQuantity" }, notifications);
    }

    [Fact]
    public void Removed_item_changes_do_not_notify_cart_total()
    {
        // Given
        var cart = CreateCart();
        Invoke(cart, "AddToCart", CreateMenu(1, "Coffee", 2500, 20), 1);
        var item = Assert.Single(Items(cart));
        Invoke(cart, "RemoveFromCart", item);
        var notifications = Observe(cart);

        // When
        Write(item, "Quantity", 2);

        // Then
        Assert.Empty(notifications);
        Assert.Equal(0, Read<int>(cart, "TotalPrice"));
    }

    [Fact]
    public void Clearing_cart_preserves_collection_identity_and_detaches_items()
    {
        // Given
        var cart = CreateCart();
        var collection = Read<object>(cart, "CartItems");
        Invoke(cart, "AddToCart", CreateMenu(1, "Coffee", 2500, 20), 1);
        var removedItem = Assert.Single(Items(cart));
        var notifications = Observe(cart);

        // When
        Assert.IsAssignableFrom<IList>(collection).Clear();

        // Then
        Assert.Same(collection, Read<object>(cart, "CartItems"));
        Assert.Equal(new[] { "TotalPrice", "TotalQuantity" }, notifications);
        notifications.Clear();
        Write(removedItem, "Quantity", 2);
        Assert.Empty(notifications);
    }

    [Fact]
    public void TotalQuantity_sums_quantities_across_distinct_lines()
    {
        // Given
        var cart = CreateCart();
        Invoke(cart, "AddToCart", CreateMenu(1, "Coffee", 2500, 20), 1);
        Invoke(cart, "AddToCart", CreateMenu(2, "Tea", 3000, 20), 2);
        Invoke(cart, "AddToCart", CreateMenu(3, "Cake", 4000, 20), 1);

        // When
        var totalQuantity = Read<int>(cart, "TotalQuantity");

        // Then
        Assert.Equal(4, totalQuantity);
        Assert.Equal(3, Items(cart).Count);
    }

    [Fact]
    public void TotalQuantity_throws_when_sum_overflows()
    {
        // Given
        var cart = CreateCart();
        Invoke(cart, "AddToCart", CreateMenu(1, "Coffee", 1, int.MaxValue), int.MaxValue);
        Invoke(cart, "AddToCart", CreateMenu(2, "Tea", 1, 1), 1);

        // When
        var exception = Assert.Throws<TargetInvocationException>(() => Read<int>(cart, "TotalQuantity"));

        // Then
        Assert.IsType<OverflowException>(exception.InnerException);
    }

    private static object CreateCart() =>
        Assert.IsAssignableFrom<object>(Activator.CreateInstance(RequireType("KioskProject.ViewModels.CartViewModel")));

    private static object CreateMenu(int id, string name, int price, int stock)
    {
        var menu = Assert.IsAssignableFrom<object>(Activator.CreateInstance(RequireType("KioskProject.Models.MenuItem")));
        Write(menu, "Id", id);
        Write(menu, "Name", name);
        Write(menu, "Price", price);
        Write(menu, "Stock", stock);
        return menu;
    }

    private static object CreateItem(object menu, int quantity)
    {
        var item = Assert.IsAssignableFrom<object>(Activator.CreateInstance(RequireType("KioskProject.Models.CartItem")));
        Write(item, "Menu", menu);
        Write(item, "Quantity", quantity);
        return item;
    }

    private static List<string?> Observe(object cart)
    {
        var notifications = new List<string?>();
        Assert.IsAssignableFrom<INotifyPropertyChanged>(cart).PropertyChanged +=
            (_, args) => notifications.Add(args.PropertyName);
        return notifications;
    }

    private static IReadOnlyList<object> Items(object cart) =>
        Assert.IsAssignableFrom<IEnumerable>(Read<object>(cart, "CartItems")).Cast<object>().ToArray();

    private static void Invoke(object target, string method, params object?[] arguments) =>
        Assert.IsAssignableFrom<MethodInfo>(target.GetType().GetMethod(method)).Invoke(target, arguments);

    private static T Read<T>(object target, string property) =>
        Assert.IsAssignableFrom<T>(Assert.IsAssignableFrom<PropertyInfo>(target.GetType().GetProperty(property)).GetValue(target));

    private static void Write(object target, string property, object? value) =>
        Assert.IsAssignableFrom<PropertyInfo>(target.GetType().GetProperty(property)).SetValue(target, value);

    private static Type RequireType(string fullName) =>
        Assert.IsAssignableFrom<Type>(ProductAssembly.GetType(fullName));
}
