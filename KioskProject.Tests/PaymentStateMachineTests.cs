using System.ComponentModel;

namespace KioskProject.Tests;

public sealed class PaymentStateMachineTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("card")]
    public void Payment_does_not_save_without_an_explicit_supported_method(string? paymentMethod)
    {
        // Given
        var fixture = ReadyForPayment();
        var complete = fixture.Command("CompletePaymentCommand");

        // When
        fixture.Execute("SelectPaymentMethodCommand", paymentMethod);
        complete.Execute(null);

        // Then
        Assert.Equal(0, fixture.DataService.SaveAttempts);
        Assert.Equal("Payment", fixture.State("CurrentView"));
        Assert.Equal("Selecting", fixture.State("PaymentPhase"));
        Assert.Single(fixture.CartItems());
        Assert.False(complete.CanExecute(null));
    }

    [Theory]
    [InlineData("Pay")]
    [InlineData("Face")]
    public void Explicit_method_selection_enables_payment_and_notifies_status(string paymentMethod)
    {
        // Given
        var fixture = ReadyForPayment();
        var complete = fixture.Command("CompletePaymentCommand");
        var propertyNotifications = MainViewModelFixture.Observe(fixture.ViewModel);
        var enablementNotifications = 0;
        complete.CanExecuteChanged += (_, _) => enablementNotifications++;

        // When
        fixture.Execute("SelectPaymentMethodCommand", paymentMethod);

        // Then
        Assert.Equal(paymentMethod, MainViewModelFixture.Read<string>(fixture.ViewModel, "SelectedPaymentMethod"));
        Assert.True(complete.CanExecute(null));
        Assert.Contains("SelectedPaymentMethod", propertyNotifications);
        Assert.True(enablementNotifications > 0);
    }

    [Fact]
    public void Successful_payment_saves_deep_snapshot_before_stock_and_cart_commit()
    {
        // Given
        var fixture = ReadyForPayment();
        var cartCollection = MainViewModelFixture.Read<object>(fixture.Cart, "CartItems");
        var stockNotifications = new List<string?>();
        Assert.IsAssignableFrom<INotifyPropertyChanged>(fixture.MenuItem).PropertyChanged +=
            (_, args) => stockNotifications.Add(args.PropertyName);
        fixture.DataService.OnSave = order =>
        {
            Assert.Equal("Saving", fixture.State("PaymentPhase"));
            Assert.Equal(20, MainViewModelFixture.Read<int>(fixture.MenuItem, "Stock"));
            Assert.Single(fixture.CartItems());
            Assert.Equal("Pay", MainViewModelFixture.Read<string>(order, "PaymentMethod"));
        };
        fixture.Execute("SelectPaymentMethodCommand", "Pay");

        // When
        fixture.Execute("CompletePaymentCommand");

        // Then
        var savedOrder = Assert.Single(fixture.DataService.SavedOrders);
        var savedItem = Assert.Single(MainViewModelFixture.OrderItems(savedOrder));
        var savedMenu = MainViewModelFixture.Read<object>(savedItem, "Menu");
        Assert.Equal(1, MainViewModelFixture.Read<int>(savedItem, "Quantity"));
        Assert.Equal(2500, MainViewModelFixture.Read<int>(savedOrder, "TotalPrice"));
        Assert.Equal(20, MainViewModelFixture.Read<int>(savedMenu, "Stock"));
        Assert.NotSame(fixture.MenuItem, savedMenu);
        Assert.Equal(19, MainViewModelFixture.Read<int>(fixture.MenuItem, "Stock"));
        Assert.Equal(new[] { "Stock" }, stockNotifications);
        Assert.Empty(fixture.CartItems());
        Assert.Same(cartCollection, MainViewModelFixture.Read<object>(fixture.Cart, "CartItems"));
        Assert.Equal("Payment", fixture.State("CurrentView"));
        Assert.Equal("Success", fixture.State("PaymentPhase"));
        Assert.True(MainViewModelFixture.Read<bool>(fixture.ViewModel, "IsPaymentSuccessful"));
        Assert.Same(savedOrder, MainViewModelFixture.Read<object>(fixture.ViewModel, "LastOrder"));
    }

    [Fact]
    public void Save_failure_preserves_transaction_state_and_retry_releases_reentry_guard()
    {
        // Given
        var fixture = ReadyForPayment();
        fixture.Execute("SelectPaymentMethodCommand", "Pay");
        fixture.DataService.SaveException = new IOException("disk full");

        // When
        fixture.Execute("CompletePaymentCommand");

        // Then
        Assert.Equal(1, fixture.DataService.SaveAttempts);
        Assert.Empty(fixture.DataService.SavedOrders);
        Assert.Equal("Payment", fixture.State("CurrentView"));
        Assert.Equal("Selecting", fixture.State("PaymentPhase"));
        Assert.Equal("Pay", MainViewModelFixture.Read<string>(fixture.ViewModel, "SelectedPaymentMethod"));
        Assert.Equal("disk full", MainViewModelFixture.Read<string>(fixture.ViewModel, "PaymentError"));
        Assert.Equal(20, MainViewModelFixture.Read<int>(fixture.MenuItem, "Stock"));
        Assert.Single(fixture.CartItems());
        Assert.True(fixture.Command("CompletePaymentCommand").CanExecute(null));

        // When
        fixture.DataService.SaveException = null;
        fixture.DataService.OnSave = _ => fixture.Execute("CompletePaymentCommand");
        fixture.Execute("CompletePaymentCommand");

        // Then
        Assert.Equal(2, fixture.DataService.SaveAttempts);
        Assert.Single(fixture.DataService.SavedOrders);
        Assert.Equal(19, MainViewModelFixture.Read<int>(fixture.MenuItem, "Stock"));
        Assert.Empty(fixture.CartItems());
        Assert.Equal("Success", fixture.State("PaymentPhase"));
    }

    [Fact]
    public void Postsave_stock_observer_fault_finalizes_committed_order_without_retry()
    {
        // Given
        var fixture = ReadyForPaymentWithPostsaveFault();

        // When
        fixture.Execute("CompletePaymentCommand");

        // Then
        Assert.Equal(1, fixture.DataService.SaveAttempts);
        Assert.Single(fixture.DataService.SavedOrders);
        Assert.Equal(19, MainViewModelFixture.Read<int>(fixture.MenuItem, "Stock"));
        Assert.Empty(fixture.CartItems());
        Assert.Equal("Success", fixture.State("PaymentPhase"));
        Assert.False(fixture.Command("CompletePaymentCommand").CanExecute(null));
        Assert.Contains("postsave_fault", MainViewModelFixture.Read<string>(fixture.ViewModel, "PaymentError"));
    }

    [Fact]
    public void Postsave_stock_observer_fault_blocks_subsequent_duplicate_payment()
    {
        // Given
        var fixture = ReadyForPaymentWithPostsaveFault();
        fixture.Execute("CompletePaymentCommand");

        // When
        fixture.Execute("CompletePaymentCommand");

        // Then
        Assert.Equal(1, fixture.DataService.SaveAttempts);
        Assert.Single(fixture.DataService.SavedOrders);
        Assert.Equal(19, MainViewModelFixture.Read<int>(fixture.MenuItem, "Stock"));
        Assert.Empty(fixture.CartItems());
        Assert.Equal("Success", fixture.State("PaymentPhase"));
    }

    [Fact]
    public void Reentrant_double_submit_saves_exactly_one_order()
    {
        // Given
        var fixture = ReadyForPayment();
        fixture.Execute("SelectPaymentMethodCommand", "Pay");
        fixture.DataService.OnSave = _ => MainViewModelFixture.Invoke(fixture.ViewModel, "CompletePayment");

        // When
        fixture.Execute("CompletePaymentCommand");

        // Then
        Assert.Equal(1, fixture.DataService.SaveAttempts);
        Assert.Single(fixture.DataService.SavedOrders);
        Assert.Equal("Success", fixture.State("PaymentPhase"));
    }

    [Fact]
    public void Acknowledgement_resets_payment_state_before_menu_and_second_order_has_no_stale_state()
    {
        // Given
        var fixture = ReadyForPayment();
        fixture.Execute("SelectPaymentMethodCommand", "Pay");
        fixture.Execute("CompletePaymentCommand");
        var resetObservedBeforeMenu = false;
        Assert.IsAssignableFrom<INotifyPropertyChanged>(fixture.ViewModel).PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == "CurrentView")
            {
                resetObservedBeforeMenu =
                    fixture.State("PaymentPhase") == "Selecting" &&
                    MainViewModelFixture.Read<string>(fixture.ViewModel, "SelectedPaymentMethod").Length == 0 &&
                    MainViewModelFixture.ReadNullable(fixture.ViewModel, "LastOrder") is null;
            }
        };

        // When
        fixture.Execute("AcknowledgePaymentCommand");

        // Then
        Assert.True(resetObservedBeforeMenu);
        Assert.Equal("Menu", fixture.State("CurrentView"));
        Assert.True(MainViewModelFixture.Read<bool>(fixture.ViewModel, "IsStartScreen"));
        Assert.Equal("Selecting", fixture.State("PaymentPhase"));
        Assert.Equal(string.Empty, MainViewModelFixture.Read<string>(fixture.ViewModel, "SelectedPaymentMethod"));
        Assert.Equal(string.Empty, MainViewModelFixture.Read<string>(fixture.ViewModel, "PaymentError"));
        Assert.False(MainViewModelFixture.Read<bool>(fixture.ViewModel, "IsPaymentSuccessful"));
        Assert.Null(MainViewModelFixture.ReadNullable(fixture.ViewModel, "LastOrder"));

        // When
        fixture.Execute("StartOrderCommand");
        fixture.Add();
        fixture.Execute("ShowCartCommand");
        fixture.Execute("ShowPaymentCommand");

        // Then
        Assert.Equal("Selecting", fixture.State("PaymentPhase"));
        Assert.Equal(string.Empty, MainViewModelFixture.Read<string>(fixture.ViewModel, "SelectedPaymentMethod"));
        Assert.Equal(string.Empty, MainViewModelFixture.Read<string>(fixture.ViewModel, "PaymentError"));

        // When
        fixture.Execute("SelectPaymentMethodCommand", "Face");
        fixture.Execute("CompletePaymentCommand");

        // Then
        Assert.Equal(2, fixture.DataService.SavedOrders.Count);
        Assert.Equal(new[] { "Pay", "Face" }, fixture.DataService.SavedOrders.Select(
            order => MainViewModelFixture.Read<string>(order, "PaymentMethod")));
        Assert.Equal(18, MainViewModelFixture.Read<int>(fixture.MenuItem, "Stock"));
        Assert.Equal("Success", fixture.State("PaymentPhase"));
    }

    private static MainViewModelFixture ReadyForPaymentWithPostsaveFault()
    {
        var fixture = ReadyForPayment();
        fixture.Execute("SelectPaymentMethodCommand", "Pay");
        Assert.IsAssignableFrom<INotifyPropertyChanged>(fixture.MenuItem).PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == "Stock")
            {
                throw new InvalidOperationException("postsave_fault");
            }
        };
        return fixture;
    }

    private static MainViewModelFixture ReadyForPayment()
    {
        var fixture = MainViewModelFixture.Create();
        fixture.Execute("StartOrderCommand");
        fixture.Add();
        fixture.Execute("ShowCartCommand");
        fixture.Execute("ShowPaymentCommand");
        return fixture;
    }
}
