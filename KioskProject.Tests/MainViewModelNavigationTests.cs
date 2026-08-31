using System.Collections;

namespace KioskProject.Tests;

public sealed class MainViewModelNavigationTests
{
    [Fact]
    public void Constructor_composes_one_shared_cart_and_starts_on_menu()
    {
        // Given / When
        var fixture = MainViewModelFixture.Create();

        // Then
        Assert.Equal(new[] { "Menu", "Cart", "Payment" },
            Enum.GetNames(MainViewModelFixture.RequireType("KioskProject.ViewModels.KioskView")));
        Assert.Equal(new[] { "Selecting", "Saving", "Success" },
            Enum.GetNames(MainViewModelFixture.RequireType("KioskProject.ViewModels.PaymentPhase")));
        Assert.Equal("Menu", fixture.State("CurrentView"));
        Assert.True(MainViewModelFixture.Read<bool>(fixture.ViewModel, "IsStartScreen"));
        Assert.Same(fixture.Cart, MainViewModelFixture.Read<object>(fixture.Menu, "Cart"));
        Assert.Same(fixture.DataService.Proxy, MainViewModelFixture.Read<object>(fixture.ViewModel, "DataService"));
    }

    [Fact]
    public void Menu_and_cart_navigation_preserves_cart_and_notifies_direct_state()
    {
        // Given
        var fixture = MainViewModelFixture.Create();
        var cartCollection = MainViewModelFixture.Read<object>(fixture.Cart, "CartItems");
        var notifications = MainViewModelFixture.Observe(fixture.ViewModel);
        fixture.Execute("StartOrderCommand");
        fixture.Add();

        // When
        fixture.Execute("ShowCartCommand");
        fixture.Execute("ShowMenuCommand");

        // Then
        Assert.Equal("Menu", fixture.State("CurrentView"));
        Assert.False(MainViewModelFixture.Read<bool>(fixture.ViewModel, "IsStartScreen"));
        Assert.Same(cartCollection, MainViewModelFixture.Read<object>(fixture.Cart, "CartItems"));
        Assert.Single(fixture.CartItems());
        Assert.Equal(new[] { "IsStartScreen", "CurrentView", "CurrentView" }, notifications);
    }

    [Fact]
    public void Empty_cart_blocks_checkout_until_collection_change_enables_command()
    {
        // Given
        var fixture = MainViewModelFixture.Create();
        fixture.Execute("StartOrderCommand");
        fixture.Execute("ShowCartCommand");
        var checkout = fixture.Command("ShowPaymentCommand");
        var enablementNotifications = 0;
        checkout.CanExecuteChanged += (_, _) => enablementNotifications++;

        // When
        checkout.Execute(null);

        // Then
        Assert.False(checkout.CanExecute(null));
        Assert.Equal("Cart", fixture.State("CurrentView"));
        Assert.Equal(0, fixture.DataService.SaveAttempts);

        // When
        fixture.Add();

        // Then
        Assert.True(checkout.CanExecute(null));
        Assert.True(enablementNotifications > 0);

        // When
        checkout.Execute(null);

        // Then
        Assert.Equal("Payment", fixture.State("CurrentView"));
        Assert.Equal("Selecting", fixture.State("PaymentPhase"));
        Assert.Equal(string.Empty, MainViewModelFixture.Read<string>(fixture.ViewModel, "SelectedPaymentMethod"));
        Assert.Equal(string.Empty, MainViewModelFixture.Read<string>(fixture.ViewModel, "PaymentError"));
    }

    [Fact]
    public void Back_to_cart_preserves_cart_and_reentry_starts_fresh_selection()
    {
        // Given
        var fixture = MainViewModelFixture.Create();
        fixture.Add();
        fixture.Execute("ShowCartCommand");
        fixture.Execute("ShowPaymentCommand");
        fixture.Execute("SelectPaymentMethodCommand", "Pay");
        fixture.DataService.SaveException = new IOException("offline");
        fixture.Execute("CompletePaymentCommand");
        var cartCollection = MainViewModelFixture.Read<object>(fixture.Cart, "CartItems");

        // When
        fixture.Execute("BackToCartCommand");

        // Then
        Assert.Equal("Cart", fixture.State("CurrentView"));
        Assert.Single(fixture.CartItems());
        Assert.Same(cartCollection, MainViewModelFixture.Read<object>(fixture.Cart, "CartItems"));
        Assert.Equal(string.Empty, MainViewModelFixture.Read<string>(fixture.ViewModel, "PaymentError"));

        // When
        fixture.Execute("ShowPaymentCommand");

        // Then
        Assert.Equal("Payment", fixture.State("CurrentView"));
        Assert.Equal("Selecting", fixture.State("PaymentPhase"));
        Assert.Equal(string.Empty, MainViewModelFixture.Read<string>(fixture.ViewModel, "SelectedPaymentMethod"));
        Assert.Null(MainViewModelFixture.ReadNullable(fixture.ViewModel, "LastOrder"));
    }

    [Fact]
    public void Clear_cart_command_clears_existing_collection_and_disables_checkout()
    {
        // Given
        var fixture = MainViewModelFixture.Create();
        fixture.Add();
        fixture.Execute("ShowCartCommand");
        var collection = MainViewModelFixture.Read<object>(fixture.Cart, "CartItems");

        // When
        fixture.Execute("ClearCartCommand");

        // Then
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable>(collection));
        Assert.Same(collection, MainViewModelFixture.Read<object>(fixture.Cart, "CartItems"));
        Assert.False(fixture.Command("ShowPaymentCommand").CanExecute(null));
    }
}
