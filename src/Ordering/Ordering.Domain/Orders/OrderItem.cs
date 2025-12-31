using NetCommerce.SharedKernel.Domain;

namespace NetCommerce.Ordering.Domain.Orders;

/// <summary>
/// Order line item with Price Snapshotting.
/// AppliedPrice and AppliedTitle are captured at order time and never change.
/// </summary>
public sealed class OrderItem : Entity<Guid>
{
    public Guid ProductId { get; private set; }
    
    /// <summary>
    /// Product name captured at order time (snapshot).
    /// </summary>
    public string AppliedTitle { get; private set; } = string.Empty;
    
    /// <summary>
    /// Price captured at order time (snapshot).
    /// </summary>
    public Money AppliedPrice { get; private set; } = default!;
    
    public int Quantity { get; private set; }
    public string? Sku { get; private set; }

    /// <summary>
    /// Calculated line total.
    /// </summary>
    public Money LineTotal => AppliedPrice.Multiply(Quantity);

    internal OrderItem(
        Guid id,
        Guid productId,
        string appliedTitle,
        Money appliedPrice,
        int quantity,
        string? sku)
    {
        Id = id;
        ProductId = productId;
        AppliedTitle = appliedTitle;
        AppliedPrice = appliedPrice;
        Quantity = quantity;
        Sku = sku;
    }

    private OrderItem() { }

    internal void UpdateQuantity(int newQuantity)
    {
        if (newQuantity <= 0)
            throw new ArgumentException("Quantity must be positive", nameof(newQuantity));
        
        Quantity = newQuantity;
    }
}
