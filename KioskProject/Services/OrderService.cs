using KioskProject.Models;

namespace KioskProject.Services;

public class OrderService
{
    private readonly IDataService _dataService;
    private readonly Func<string> _createId;
    private readonly Func<DateTime> _getCurrentTime;

    public OrderService(IDataService dataService)
        : this(
            dataService,
            () => Guid.NewGuid().ToString("N"),
            () => DateTime.UtcNow)
    {
    }

    internal OrderService(
        IDataService dataService,
        Func<string> createId,
        Func<DateTime> getCurrentTime)
    {
        ArgumentNullException.ThrowIfNull(dataService);
        ArgumentNullException.ThrowIfNull(createId);
        ArgumentNullException.ThrowIfNull(getCurrentTime);
        _dataService = dataService;
        _createId = createId;
        _getCurrentTime = getCurrentTime;
    }

    public Order SaveOrder(
        IReadOnlyCollection<CartItem> items,
        int totalPrice,
        string paymentMethod)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one item.", nameof(items));
        }

        if (string.IsNullOrWhiteSpace(paymentMethod))
        {
            throw new ArgumentException("A payment method is required.", nameof(paymentMethod));
        }

        var snapshotItems = new List<CartItem>(items.Count);
        foreach (var sourceItem in items)
        {
            if (sourceItem is null || sourceItem.Menu is null)
            {
                throw new ArgumentException("Order items and menus cannot be null.", nameof(items));
            }

            var sourceMenu = sourceItem.Menu;
            snapshotItems.Add(new CartItem
            {
                Menu = new MenuItem
                {
                    Id = sourceMenu.Id,
                    Name = sourceMenu.Name,
                    Price = sourceMenu.Price,
                    Category = sourceMenu.Category,
                    ImagePath = sourceMenu.ImagePath,
                    Stock = sourceMenu.Stock
                },
                Quantity = sourceItem.Quantity
            });
        }

        var calculatedTotal = 0;
        foreach (var item in snapshotItems)
        {
            calculatedTotal = checked(calculatedTotal + item.Subtotal);
        }

        if (calculatedTotal != totalPrice)
        {
            throw new ArgumentException("The order total does not match its items.", nameof(totalPrice));
        }

        var order = new Order
        {
            Id = _createId(),
            OrderedAt = _getCurrentTime(),
            Items = snapshotItems,
            TotalPrice = totalPrice,
            PaymentMethod = paymentMethod
        };
        _dataService.SaveOrder(order);
        return order;
    }
}
