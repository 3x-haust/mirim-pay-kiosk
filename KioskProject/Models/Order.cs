namespace KioskProject.Models;

public class Order
{
    public string Id { get; set; } = string.Empty;

    public DateTime OrderedAt { get; set; }

    public List<CartItem> Items { get; set; } = new();

    public int TotalPrice { get; set; }

    public string PaymentMethod { get; set; } = string.Empty;
}
