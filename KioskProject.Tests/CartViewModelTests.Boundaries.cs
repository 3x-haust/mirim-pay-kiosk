using System.Reflection;

namespace KioskProject.Tests;

public sealed partial class CartViewModelTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(20, 21)]
    [InlineData(int.MaxValue - 1, int.MaxValue)]
    public void AddToCart_refuses_stock_unavailable_request_without_partial_mutation(int stock, int quantity)
    {
        // Given
        var cart = CreateCart();
        var menu = CreateMenu(1, "Coffee", 2500, stock);
        var notifications = Observe(cart);

        // When
        Invoke(cart, "AddToCart", menu, quantity);

        // Then
        Assert.Empty(Items(cart));
        Assert.Equal(0, Read<int>(cart, "TotalPrice"));
        Assert.Equal("maximum-stock", Read<string>(cart, "Status"));
        Assert.Equal(new[] { "Status" }, notifications);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void AddToCart_refuses_nonpositive_quantity_without_partial_mutation(int quantity)
    {
        // Given
        var cart = CreateCart();
        var notifications = Observe(cart);

        // When
        Invoke(cart, "AddToCart", CreateMenu(1, "Coffee", 2500, 20), quantity);

        // Then
        Assert.Empty(Items(cart));
        Assert.Equal("invalid-quantity", Read<string>(cart, "Status"));
        Assert.Equal(new[] { "Status" }, notifications);
    }

    [Fact]
    public void AddToCart_refuses_null_menu_without_mutation()
    {
        // Given
        var cart = CreateCart();
        var notifications = Observe(cart);

        // When
        Invoke(cart, "AddToCart", null, 1);

        // Then
        Assert.Empty(Items(cart));
        Assert.Equal("invalid-item", Read<string>(cart, "Status"));
        Assert.Equal(new[] { "Status" }, notifications);
    }

    [Fact]
    public void AddToCart_refuses_aggregate_above_stock_without_partial_mutation()
    {
        // Given
        var cart = CreateCart();
        var menu = CreateMenu(1, "Coffee", 2500, 20);
        Invoke(cart, "AddToCart", menu, 19);
        var item = Assert.Single(Items(cart));
        var notifications = Observe(cart);

        // When
        Invoke(cart, "AddToCart", CreateMenu(1, "Other instance", 9999, 20), 2);

        // Then
        Assert.Same(menu, Read<object>(item, "Menu"));
        Assert.Equal(19, Read<int>(item, "Quantity"));
        Assert.Equal(47500, Read<int>(cart, "TotalPrice"));
        Assert.Equal("maximum-stock", Read<string>(cart, "Status"));
        Assert.Equal(new[] { "Status" }, notifications);
    }

    [Fact]
    public void AddToCart_refuses_overflowing_aggregate_without_partial_mutation()
    {
        // Given
        var cart = CreateCart();
        Invoke(cart, "AddToCart", CreateMenu(1, "Coffee", 1, int.MaxValue), 1);
        var item = Assert.Single(Items(cart));
        var notifications = Observe(cart);

        // When
        Invoke(cart, "AddToCart", CreateMenu(1, "Coffee", 1, int.MaxValue), int.MaxValue);

        // Then
        Assert.Equal(1, Read<int>(item, "Quantity"));
        Assert.Equal("maximum-stock", Read<string>(cart, "Status"));
        Assert.Equal(new[] { "Status" }, notifications);
    }

    [Fact]
    public void ChangeQuantity_refuses_target_above_stock_without_partial_mutation()
    {
        // Given
        var cart = CreateCart();
        Invoke(cart, "AddToCart", CreateMenu(1, "Coffee", 2500, 20), 19);
        var item = Assert.Single(Items(cart));
        var notifications = Observe(cart);

        // When
        Invoke(cart, "ChangeQuantity", item, 2);

        // Then
        Assert.Equal(19, Read<int>(item, "Quantity"));
        Assert.Equal(47500, Read<int>(cart, "TotalPrice"));
        Assert.Equal("maximum-stock", Read<string>(cart, "Status"));
        Assert.Equal(new[] { "Status" }, notifications);
    }

    [Fact]
    public void ChangeQuantity_refuses_overflowing_target_without_partial_mutation()
    {
        // Given
        var cart = CreateCart();
        Invoke(cart, "AddToCart", CreateMenu(1, "Coffee", 1, int.MaxValue), 1);
        var item = Assert.Single(Items(cart));
        var notifications = Observe(cart);

        // When
        Invoke(cart, "ChangeQuantity", item, int.MaxValue);

        // Then
        Assert.Equal(1, Read<int>(item, "Quantity"));
        Assert.Equal("maximum-stock", Read<string>(cart, "Status"));
        Assert.Equal(new[] { "Status" }, notifications);
    }

    [Fact]
    public void ChangeQuantity_ignores_nonmember_without_notifications()
    {
        // Given
        var cart = CreateCart();
        var item = CreateItem(CreateMenu(1, "Coffee", 2500, 20), 1);
        var notifications = Observe(cart);

        // When
        Invoke(cart, "ChangeQuantity", item, 1);

        // Then
        Assert.Empty(Items(cart));
        Assert.Empty(notifications);
    }

    [Fact]
    public void RemoveFromCart_ignores_null_without_notifications()
    {
        // Given
        var cart = CreateCart();
        var notifications = Observe(cart);

        // When
        Invoke(cart, "RemoveFromCart", (object?)null);

        // Then
        Assert.Empty(Items(cart));
        Assert.Empty(notifications);
    }

    [Fact]
    public void RemoveFromCart_ignores_nonmember_without_notifications()
    {
        // Given
        var cart = CreateCart();
        var item = CreateItem(CreateMenu(1, "Coffee", 2500, 20), 1);
        var notifications = Observe(cart);

        // When
        Invoke(cart, "RemoveFromCart", item);

        // Then
        Assert.Empty(Items(cart));
        Assert.Empty(notifications);
    }

    [Fact]
    public void ChangeQuantity_ignores_null_without_notifications()
    {
        // Given
        var cart = CreateCart();
        var notifications = Observe(cart);

        // When
        Invoke(cart, "ChangeQuantity", null, 1);

        // Then
        Assert.Empty(Items(cart));
        Assert.Empty(notifications);
    }

    [Fact]
    public void TotalPrice_uses_checked_int_arithmetic_when_subtotal_overflows()
    {
        // Given
        var cart = CreateCart();
        Invoke(cart, "AddToCart", CreateMenu(1, "Coffee", int.MaxValue, 2), 2);

        // When
        var exception = Assert.Throws<TargetInvocationException>(() =>
            Assert.IsAssignableFrom<PropertyInfo>(cart.GetType().GetProperty("TotalPrice")).GetValue(cart));

        // Then
        Assert.IsType<OverflowException>(exception.InnerException);
    }

    [Fact]
    public void TotalPrice_uses_checked_int_arithmetic_when_sum_overflows()
    {
        // Given
        var cart = CreateCart();
        Invoke(cart, "AddToCart", CreateMenu(1, "Coffee", int.MaxValue, 1), 1);
        Invoke(cart, "AddToCart", CreateMenu(2, "Tea", 1, 1), 1);

        // When
        var exception = Assert.Throws<TargetInvocationException>(() =>
            Assert.IsAssignableFrom<PropertyInfo>(cart.GetType().GetProperty("TotalPrice")).GetValue(cart));

        // Then
        Assert.IsType<OverflowException>(exception.InnerException);
    }
}
