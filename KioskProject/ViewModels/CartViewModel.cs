using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using KioskProject.Models;

namespace KioskProject.ViewModels;

public class CartViewModel : INotifyPropertyChanged
{
    private readonly HashSet<CartItem> _subscribedItems = new();
    private string _status = string.Empty;

    public CartViewModel()
    {
        CartItems.CollectionChanged += OnCartItemsChanged;
        RemoveCommand = new CartCommand(parameter =>
        {
            if (parameter is CartItem item)
            {
                RemoveFromCart(item);
            }
        });
        IncreaseCommand = new CartCommand(parameter =>
        {
            if (parameter is CartItem item)
            {
                ChangeQuantity(item, 1);
            }
        });
        DecreaseCommand = new CartCommand(parameter =>
        {
            if (parameter is CartItem item)
            {
                ChangeQuantity(item, -1);
            }
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<CartItem> CartItems { get; } = new();

    public int TotalPrice
    {
        get
        {
            var total = 0;
            foreach (var item in CartItems)
            {
                total = checked(total + item.Subtotal);
            }

            return total;
        }
    }

    public int TotalQuantity
    {
        get
        {
            var total = 0;
            foreach (var item in CartItems)
            {
                total = checked(total + item.Quantity);
            }

            return total;
        }
    }

    public string Status
    {
        get => _status;
        private set
        {
            if (_status == value)
            {
                return;
            }

            _status = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        }
    }

    public ICommand RemoveCommand { get; }

    public ICommand IncreaseCommand { get; }

    public ICommand DecreaseCommand { get; }

    public void AddToCart(MenuItem menu, int quantity)
    {
        if (menu is null)
        {
            Status = "invalid-item";
            return;
        }

        if (quantity < 1)
        {
            Status = "invalid-quantity";
            return;
        }

        var existingItem = CartItems.FirstOrDefault(item => item.Menu.Id == menu.Id);
        var currentQuantity = existingItem?.Quantity ?? 0;
        var stock = existingItem?.Menu.Stock ?? menu.Stock;
        if (stock < currentQuantity || quantity > stock - currentQuantity)
        {
            Status = "maximum-stock";
            return;
        }

        Status = string.Empty;
        if (existingItem is null)
        {
            CartItems.Add(new CartItem { Menu = menu, Quantity = quantity });
            return;
        }

        existingItem.Quantity = checked(currentQuantity + quantity);
    }

    public void RemoveFromCart(CartItem item)
    {
        if (item is null || !CartItems.Contains(item))
        {
            return;
        }

        Status = string.Empty;
        CartItems.Remove(item);
    }

    public void ChangeQuantity(CartItem item, int delta)
    {
        if (item is null || !CartItems.Contains(item) || delta == 0)
        {
            return;
        }

        var target = (long)item.Quantity + delta;
        if (target <= 0)
        {
            RemoveFromCart(item);
            return;
        }

        if (target > item.Menu.Stock)
        {
            Status = "maximum-stock";
            return;
        }

        Status = string.Empty;
        item.Quantity = checked((int)target);
    }

    private void OnCartItemsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        foreach (var removedItem in _subscribedItems.Where(item => !CartItems.Contains(item)).ToArray())
        {
            removedItem.PropertyChanged -= OnCartItemPropertyChanged;
            _subscribedItems.Remove(removedItem);
        }

        foreach (var addedItem in CartItems.Where(item => _subscribedItems.Add(item)))
        {
            addedItem.PropertyChanged += OnCartItemPropertyChanged;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalPrice)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalQuantity)));
    }

    private void OnCartItemPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(CartItem.Quantity))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalQuantity)));
        }
        else if (args.PropertyName == nameof(CartItem.Subtotal))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalPrice)));
        }
    }

    private sealed class CartCommand(Action<object?> execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => execute(parameter);
    }
}
