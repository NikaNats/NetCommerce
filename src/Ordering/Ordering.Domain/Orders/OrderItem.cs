using NetCommerce.SharedKernel.Domain;

namespace NetCommerce.Ordering.Domain.Orders;

/// <summary>
///     Order line item with Price Snapshotting and Triple-Pass Pricing Pattern.
///     AppliedPrice, AppliedTitle, and PriceBreakdown are captured at order time and never change.
/// </summary>
public sealed class OrderItem : Entity<Guid>
{
    internal OrderItem(
        Guid id,
        Guid productId,
        string appliedTitle,
        Money appliedPrice,
        int quantity,
        decimal appliedWeightKg,
        string? sku,
        PriceBreakdown priceBreakdown)
    {
        Id = id;
        ProductId = productId;
        AppliedTitle = appliedTitle;
        AppliedPrice = appliedPrice;
        Quantity = quantity;
        AppliedWeightKg = appliedWeightKg;
        Sku = sku;
        PriceBreakdown = priceBreakdown;
    }

    private OrderItem()
    {
    }

    public Guid ProductId { get; private set; }

    /// <summary>
    ///     Product name captured at order time (snapshot).
    /// </summary>
    public string AppliedTitle { get; private set; } = string.Empty;

    /// <summary>
    ///     Price captured at order time (snapshot). This represents the final price (for backward compatibility).
    /// </summary>
    public Money AppliedPrice { get; } = default!;

    /// <summary>
    ///     Weight in kilograms captured at order time (snapshot).
    ///     Ensures shipping labels match the product weight when ordered, even if catalog changes later.
    /// </summary>
    public decimal AppliedWeightKg { get; private set; }

    public int Quantity { get; private set; }
    public string? Sku { get; private set; }

    /// <summary>
    ///     Complete pricing breakdown for audit compliance and transparency.
    ///     Stores base price, discount, tax calculation, and rates used at order time.
    /// </summary>
    public PriceBreakdown PriceBreakdown { get; private set; } = default!;

    /// <summary>
    ///     The discount amount applied to this line item (convenience property).
    /// </summary>
    public decimal DiscountAmount => PriceBreakdown.DiscountAmount * Quantity;

    /// <summary>
    ///     The tax amount applied to this line item (convenience property).
    /// </summary>
    public decimal TaxAmount => PriceBreakdown.TaxAmount * Quantity;

    /// <summary>
    ///     Calculated line total based on the final price.
    /// </summary>
    public Money LineTotal => AppliedPrice.Multiply(Quantity);

    internal void UpdateQuantity(int newQuantity)
    {
        if (newQuantity <= 0)
            throw new ArgumentException("Quantity must be positive", nameof(newQuantity));

        Quantity = newQuantity;
    }
}
