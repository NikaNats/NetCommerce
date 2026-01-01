namespace NetCommerce.Basket.Application;

/// <summary>
///     Shopping basket model stored in Redis.
/// </summary>
public class ShoppingBasket
{
    public string CustomerId { get; set; } = string.Empty;
    public List<BasketItem> Items { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime LastUpdatedAt { get; set; }

    public decimal TotalPrice => Items.Sum(i => i.Price * i.Quantity);

    public static ShoppingBasket Create(string customerId)
    {
        return new ShoppingBasket
        {
            CustomerId = customerId,
            Items = [],
            CreatedAt = DateTime.UtcNow,
            LastUpdatedAt = DateTime.UtcNow
        };
    }

    public void AddItem(BasketItem item)
    {
        var existingItem = Items.FirstOrDefault(i => i.ProductId == item.ProductId);
        if (existingItem != null)
            existingItem.Quantity += item.Quantity;
        else
            Items.Add(item);
        LastUpdatedAt = DateTime.UtcNow;
    }

    public void UpdateItemQuantity(Guid productId, int quantity)
    {
        var item = Items.FirstOrDefault(i => i.ProductId == productId);
        if (item != null)
        {
            if (quantity <= 0)
                Items.Remove(item);
            else
                item.Quantity = quantity;
            LastUpdatedAt = DateTime.UtcNow;
        }
    }

    public void RemoveItem(Guid productId)
    {
        Items.RemoveAll(i => i.ProductId == productId);
        LastUpdatedAt = DateTime.UtcNow;
    }

    public void Clear()
    {
        Items.Clear();
        LastUpdatedAt = DateTime.UtcNow;
    }
}

public class BasketItem
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? Sku { get; set; }
    public decimal Price { get; set; }
    public int Quantity { get; set; }
    public string? ImageUrl { get; set; }
}