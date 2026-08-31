using System.ComponentModel;

namespace KioskProject.Models;

public class CartItem : INotifyPropertyChanged
{
    private int _quantity = 1;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MenuItem Menu { get; set; } = new();

    public int Quantity
    {
        get => _quantity;
        set
        {
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Quantity must be at least one.");
            }

            if (_quantity == value)
            {
                return;
            }

            _quantity = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Quantity)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Subtotal)));
        }
    }

    public int Subtotal => checked(Menu.Price * Quantity);
}
