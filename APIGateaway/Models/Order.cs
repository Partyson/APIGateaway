namespace APIGateaway.Models;

public class Order
{
    public string OrderId { get; set; } = default!;
    public string ProductId { get; set; } = default!;
    public int Quantity { get; set; }
    public Product? Product { get; set; }
}