using System.ComponentModel;
using System.Windows.Input;
using KioskProject.Models;
using KioskProject.Services;

namespace KioskProject.ViewModels;

public enum KioskView
{
    Menu, Cart, Payment
}

public enum PaymentPhase
{
    Selecting, Saving, Success
}

// allow: SIZE_OK - the finite workflow keeps routing and commit invariants in one state owner.
public class MainViewModel : INotifyPropertyChanged
{
    private readonly MainCommand[] _commands = [];
    private KioskView _currentView = KioskView.Menu;
    private PaymentPhase _paymentPhase = PaymentPhase.Selecting;
    private string _selectedPaymentMethod = string.Empty;
    private string _paymentError = string.Empty;
    private Order? _lastOrder;
    private bool _isStartScreen = true;
    private int _paymentInFlight;

    public MainViewModel()
        : this(new JsonDataService())
    {
    }

    public MainViewModel(IDataService dataService)
    {
        ArgumentNullException.ThrowIfNull(dataService);

        DataService = dataService;
        Cart = new CartViewModel();
        Menu = new MenuViewModel(dataService, Cart);
        OrderService = new OrderService(dataService);

        StartOrderCommand = new MainCommand(
            _ => IsStartScreen = false,
            _ => CurrentView == KioskView.Menu && IsStartScreen && Menu.CanPurchase);
        ShowCartCommand = new MainCommand(
            _ => CurrentView = KioskView.Cart,
            _ => CurrentView == KioskView.Menu);
        ShowMenuCommand = new MainCommand(
            _ => CurrentView = KioskView.Menu,
            _ => CurrentView == KioskView.Cart);
        ClearCartCommand = new MainCommand(
            _ => Cart.CartItems.Clear(),
            _ => CurrentView == KioskView.Cart && Cart.CartItems.Count > 0);
        ShowPaymentCommand = new MainCommand(
            _ => ShowPayment(),
            _ => CurrentView == KioskView.Cart && Cart.CartItems.Count > 0);
        SelectPaymentMethodCommand = new MainCommand(
            SelectPaymentMethod,
            CanSelectPaymentMethod);
        BackToCartCommand = new MainCommand(
            _ => BackToCart(),
            _ => CurrentView == KioskView.Payment && PaymentPhase == PaymentPhase.Selecting);
        CompletePaymentCommand = new MainCommand(
            _ => CompletePayment(),
            _ => CanCompletePayment());
        AcknowledgePaymentCommand = new MainCommand(
            _ => AcknowledgePayment(),
            _ => CurrentView == KioskView.Payment && PaymentPhase == PaymentPhase.Success);

        _commands =
        [
            (MainCommand)StartOrderCommand,
            (MainCommand)ShowCartCommand,
            (MainCommand)ShowMenuCommand,
            (MainCommand)ClearCartCommand,
            (MainCommand)ShowPaymentCommand,
            (MainCommand)SelectPaymentMethodCommand,
            (MainCommand)BackToCartCommand,
            (MainCommand)CompletePaymentCommand,
            (MainCommand)AcknowledgePaymentCommand
        ];
        Cart.CartItems.CollectionChanged += (_, _) => RaiseCommandStates();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IDataService DataService { get; }

    public OrderService OrderService { get; }

    public MenuViewModel Menu { get; }

    public CartViewModel Cart { get; }

    public KioskView CurrentView
    {
        get => _currentView;
        private set => SetValue(ref _currentView, value, nameof(CurrentView));
    }

    public PaymentPhase PaymentPhase
    {
        get => _paymentPhase;
        private set
        {
            if (!SetValue(ref _paymentPhase, value, nameof(PaymentPhase)))
            {
                return;
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPaymentSuccessful)));
        }
    }

    public bool IsPaymentSuccessful => PaymentPhase == PaymentPhase.Success;

    public string SelectedPaymentMethod
    {
        get => _selectedPaymentMethod;
        private set => SetValue(ref _selectedPaymentMethod, value, nameof(SelectedPaymentMethod));
    }

    public string PaymentError
    {
        get => _paymentError;
        private set => SetValue(ref _paymentError, value, nameof(PaymentError));
    }

    public Order? LastOrder
    {
        get => _lastOrder;
        private set => SetValue(ref _lastOrder, value, nameof(LastOrder));
    }

    public bool IsStartScreen
    {
        get => _isStartScreen;
        private set => SetValue(ref _isStartScreen, value, nameof(IsStartScreen));
    }

    public ICommand StartOrderCommand { get; }

    public ICommand ShowCartCommand { get; }

    public ICommand ShowMenuCommand { get; }

    public ICommand ClearCartCommand { get; }

    public ICommand ShowPaymentCommand { get; }

    public ICommand SelectPaymentMethodCommand { get; }

    public ICommand BackToCartCommand { get; }

    public ICommand CompletePaymentCommand { get; }

    public ICommand AcknowledgePaymentCommand { get; }

    public void CompletePayment()
    {
        if (!CanCompletePayment() || Interlocked.CompareExchange(ref _paymentInFlight, 1, 0) != 0)
        {
            return;
        }

        try
        {
            var stockChanges = Cart.CartItems
                .Select(item => (Menu: item.Menu, Quantity: item.Quantity, Stock: item.Menu.Stock))
                .ToArray();
            if (stockChanges.Length == 0 || stockChanges.Any(change => change.Quantity > change.Stock))
            {
                PaymentError = "invalid-cart";
                return;
            }

            PaymentError = string.Empty;
            PaymentPhase = PaymentPhase.Saving;
            var orderItems = Cart.CartItems.ToArray();
            var totalPrice = Cart.TotalPrice;
            Order order;
            try
            {
                order = OrderService.SaveOrder(orderItems, totalPrice, SelectedPaymentMethod);
            }
            catch (Exception exception)
            {
                PaymentError = exception.Message;
                PaymentPhase = PaymentPhase.Selecting;
                return;
            }

            Exception? postCommitWarning = null;
            void Finalize(Action action)
            {
                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    postCommitWarning ??= exception;
                }
            }

            foreach (var change in stockChanges)
            {
                Finalize(() => change.Menu.Stock = checked(change.Stock - change.Quantity));
            }

            Finalize(Cart.CartItems.Clear);
            Finalize(() => LastOrder = order);
            Finalize(() => PaymentPhase = PaymentPhase.Success);
            if (postCommitWarning is not null)
            {
                Finalize(() => PaymentError = $"post-commit: {postCommitWarning.Message}");
            }
        }
        finally
        {
            Interlocked.Exchange(ref _paymentInFlight, 0);
            RaiseCommandStates();
        }
    }

    private bool CanCompletePayment() =>
        CurrentView == KioskView.Payment &&
        PaymentPhase == PaymentPhase.Selecting &&
        IsSupportedPaymentMethod(SelectedPaymentMethod) &&
        Cart.CartItems.Count > 0 &&
        Volatile.Read(ref _paymentInFlight) == 0;

    private bool CanSelectPaymentMethod(object? parameter) =>
        CurrentView == KioskView.Payment &&
        PaymentPhase == PaymentPhase.Selecting &&
        parameter is string method &&
        IsSupportedPaymentMethod(method);

    private void SelectPaymentMethod(object? parameter)
    {
        if (parameter is string method)
        {
            SelectedPaymentMethod = method;
            PaymentError = string.Empty;
        }
    }

    private void ShowPayment()
    {
        ResetPaymentState();
        CurrentView = KioskView.Payment;
    }

    private void BackToCart()
    {
        PaymentError = string.Empty;
        CurrentView = KioskView.Cart;
    }

    private void AcknowledgePayment()
    {
        ResetPaymentState();
        IsStartScreen = true;
        CurrentView = KioskView.Menu;
    }

    private void ResetPaymentState()
    {
        SelectedPaymentMethod = string.Empty;
        PaymentError = string.Empty;
        LastOrder = null;
        PaymentPhase = PaymentPhase.Selecting;
    }

    private static bool IsSupportedPaymentMethod(string method) => method is "Pay" or "Face";

    private bool SetValue<T>(ref T field, T value, string propertyName)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        RaiseCommandStates();
        return true;
    }

    private void RaiseCommandStates()
    {
        foreach (var command in _commands)
        {
            command.RaiseCanExecuteChanged();
        }
    }

    private sealed class MainCommand(
        Action<object?> execute,
        Predicate<object?> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => canExecute(parameter);

        public void Execute(object? parameter)
        {
            if (CanExecute(parameter))
            {
                execute(parameter);
            }
        }

        internal void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
