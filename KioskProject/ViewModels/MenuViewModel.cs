using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using KioskProject.Models;
using KioskProject.Services;

namespace KioskProject.ViewModels;

public class MenuViewModel : INotifyPropertyChanged
{
    public const string AllCategory = "All";

    private string _barcodeInput = string.Empty;
    private string _selectedCategory = AllCategory;
    private string _status = string.Empty;

    public MenuViewModel(IDataService dataService, CartViewModel cart)
    {
        ArgumentNullException.ThrowIfNull(dataService);
        ArgumentNullException.ThrowIfNull(cart);

        Cart = cart;
        FilteredMenus = new ObservableCollection<MenuItem>();

        try
        {
            var loadedMenus = dataService.LoadMenus().ToArray();
            Menus = Array.AsReadOnly(loadedMenus);
            Categories = Array.AsReadOnly(
                new[] { AllCategory }
                    .Concat(loadedMenus.Select(menu => menu.Category).Distinct(StringComparer.Ordinal))
                    .ToArray());
            foreach (var menu in loadedMenus)
            {
                FilteredMenus.Add(menu);
            }
        }
        catch (Exception exception)
        {
            Menus = Array.Empty<MenuItem>();
            Categories = Array.Empty<string>();
            HasLoadError = true;
            LoadErrorMessage = exception.Message;
            _status = "load-error";
        }

        AddCommand = new MenuCommand(
            parameter =>
            {
                if (parameter is MenuItem menu)
                {
                    AddToCart(menu, 1);
                }
            },
            parameter => CanPurchase && parameter is MenuItem);
        FilterCommand = new MenuCommand(
            parameter => FilterByCategory(parameter as string),
            _ => !HasLoadError);
        ResetFilterCommand = new MenuCommand(_ => ResetFilter(), _ => !HasLoadError);
        ScanBarcodeCommand = new MenuCommand(_ => ScanBarcode(), _ => CanPurchase);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<MenuItem> Menus { get; }

    public ObservableCollection<MenuItem> FilteredMenus { get; }

    public IReadOnlyList<string> Categories { get; }

    public CartViewModel Cart { get; }

    public string BarcodeInput
    {
        get => _barcodeInput;
        set => SetValue(ref _barcodeInput, value ?? string.Empty, nameof(BarcodeInput));
    }

    public string SelectedCategory
    {
        get => _selectedCategory;
        private set => SetValue(ref _selectedCategory, value, nameof(SelectedCategory));
    }

    public string Status
    {
        get => _status;
        private set => SetValue(ref _status, value, nameof(Status));
    }

    public string LoadErrorMessage { get; } = string.Empty;

    public bool HasLoadError { get; }

    public bool IsEmpty => !HasLoadError && Menus.Count == 0;

    public bool CanPurchase => !HasLoadError && Menus.Count > 0;

    public ICommand AddCommand { get; }

    public ICommand FilterCommand { get; }

    public ICommand ResetFilterCommand { get; }

    public ICommand ScanBarcodeCommand { get; }

    public void FilterByCategory(string? category)
    {
        if (HasLoadError)
        {
            return;
        }

        var selected = string.IsNullOrEmpty(category) ? AllCategory : category;
        SelectedCategory = selected;
        FilteredMenus.Clear();
        foreach (var menu in Menus)
        {
            if (selected == AllCategory || string.Equals(menu.Category, selected, StringComparison.Ordinal))
            {
                FilteredMenus.Add(menu);
            }
        }
    }

    public void ResetFilter() => FilterByCategory(AllCategory);

    public void AddToCart(MenuItem menu, int quantity)
    {
        if (!CanPurchase)
        {
            return;
        }

        Cart.AddToCart(menu, quantity);
        Status = Cart.Status;
    }

    public void ScanBarcode()
    {
        if (!CanPurchase)
        {
            return;
        }

        if (!int.TryParse(BarcodeInput, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            Status = "invalid-barcode";
            return;
        }

        var menu = Menus.FirstOrDefault(candidate => candidate.Id == id);
        if (menu is null)
        {
            Status = "unknown-barcode";
            return;
        }

        var existing = Cart.CartItems.FirstOrDefault(item => item.Menu.Id == menu.Id);
        var stock = existing?.Menu.Stock ?? menu.Stock;
        if (stock <= (existing?.Quantity ?? 0))
        {
            Status = "out-of-stock";
            return;
        }

        Cart.AddToCart(menu, 1);
        Status = Cart.Status == "maximum-stock" ? "out-of-stock" : Cart.Status;
        if (Status.Length == 0)
        {
            BarcodeInput = string.Empty;
        }
    }

    private void SetValue(ref string field, string value, string propertyName)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class MenuCommand(
        Action<object?> execute,
        Predicate<object?> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => canExecute(parameter);

        public void Execute(object? parameter)
        {
            if (CanExecute(parameter))
            {
                execute(parameter);
            }
        }
    }
}
