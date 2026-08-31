using System.ComponentModel;
using System.Reflection;

namespace KioskProject.Tests;

public sealed class CartItemNotificationTests
{
    private static readonly Assembly ProductAssembly = ProductAssemblyFixture.Assembly;

    [Fact]
    public void Quantity_emits_quantity_then_subtotal_when_value_changes()
    {
        // Given
        var (cart, quantityProperty, _) = CreateCart(price: 2500, quantity: 1);
        var notifications = new List<string?>();
        Assert.IsAssignableFrom<INotifyPropertyChanged>(cart).PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        // When
        quantityProperty.SetValue(cart, 2);

        // Then
        Assert.Equal(new[] { "Quantity", "Subtotal" }, notifications);
        Assert.Equal(2, quantityProperty.GetValue(cart));
    }

    [Fact]
    public void Quantity_emits_nothing_when_assignment_is_unchanged()
    {
        // Given
        var (cart, quantityProperty, _) = CreateCart(price: 2500, quantity: 2);
        var notifications = new List<string?>();
        Assert.IsAssignableFrom<INotifyPropertyChanged>(cart).PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        // When
        quantityProperty.SetValue(cart, 2);

        // Then
        Assert.Empty(notifications);
        Assert.Equal(2, quantityProperty.GetValue(cart));
    }

    [Fact]
    public void Quantity_preserves_state_and_emits_nothing_when_value_is_invalid()
    {
        // Given
        var (cart, quantityProperty, _) = CreateCart(price: 2500, quantity: 2);
        var notifications = new List<string?>();
        Assert.IsAssignableFrom<INotifyPropertyChanged>(cart).PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        // When
        try
        {
            quantityProperty.SetValue(cart, 0);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is ArgumentOutOfRangeException)
        {
        }

        // Then
        Assert.Empty(notifications);
        Assert.Equal(2, quantityProperty.GetValue(cart));
    }

    [Fact]
    public void Subtotal_uses_checked_int_arithmetic_when_value_overflows()
    {
        // Given
        var (cart, _, subtotalProperty) = CreateCart(price: int.MaxValue, quantity: 2);

        // When
        var exception = Assert.Throws<TargetInvocationException>(() => subtotalProperty.GetValue(cart));

        // Then
        Assert.IsType<OverflowException>(exception.InnerException);
    }

    private static (object Cart, PropertyInfo Quantity, PropertyInfo Subtotal) CreateCart(int price, int quantity)
    {
        var menuType = RequireProductType("KioskProject.Models.MenuItem");
        var cartType = RequireProductType("KioskProject.Models.CartItem");
        var menu = Assert.IsAssignableFrom<object>(Activator.CreateInstance(menuType));
        Assert.IsAssignableFrom<PropertyInfo>(menuType.GetProperty("Price")).SetValue(menu, price);
        var cart = Assert.IsAssignableFrom<object>(Activator.CreateInstance(cartType));
        Assert.IsAssignableFrom<PropertyInfo>(cartType.GetProperty("Menu")).SetValue(cart, menu);
        var quantityProperty = Assert.IsAssignableFrom<PropertyInfo>(cartType.GetProperty("Quantity"));
        quantityProperty.SetValue(cart, quantity);
        return (cart, quantityProperty, Assert.IsAssignableFrom<PropertyInfo>(cartType.GetProperty("Subtotal")));
    }

    private static Type RequireProductType(string fullName) =>
        Assert.IsAssignableFrom<Type>(ProductAssembly.GetType(fullName));
}
