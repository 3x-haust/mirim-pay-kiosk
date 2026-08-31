using System.ComponentModel;

namespace KioskProject.Models;

public class MenuItem : INotifyPropertyChanged
{
    private int _stock;

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Price { get; set; }

    public string Category { get; set; } = string.Empty;

    public string ImagePath { get; set; } = string.Empty;

    public int Stock
    {
        get => _stock;
        set
        {
            if (_stock == value)
            {
                return;
            }

            _stock = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Stock)));
        }
    }
}
