using NetCommerce.Catalog.Domain.Products;
using NetCommerce.Inventory.Domain.Stock;
using NetCommerce.Ordering.Domain.Orders;
using NetCommerce.SharedKernel.Domain;

namespace NetCommerce.Domain.Tests.Fakers;

public static class StockFaker
{
    public static Stock Generate(int quantity = 100)
    {
        var f = new Faker();
        return Stock.Create(Guid.NewGuid(), f.Commerce.Ean13(), quantity, 10);
    }
}

public static class ProductFaker
{
    public static Product Generate()
    {
        var f = new Faker();
        return Product.Create(f.Commerce.ProductName(), f.Commerce.ProductDescription(), f.Commerce.Ean13(), Money.Create(f.Random.Decimal(10, 1000), "USD"), Guid.NewGuid());
    }
}

public static class OrderFaker
{
    public static Order Generate()
    {
        var f = new Faker();
        var customerId = Guid.NewGuid();
        var shippingAddress = ShippingAddressFaker.Generate();
        var idempotencyKey = Guid.NewGuid().ToString();
        return Order.Create(customerId, shippingAddress, idempotencyKey);
    }

    public static Order GenerateWithItems(int itemCount = 1)
    {
        var order = Generate();
        var f = new Faker();
        for (int i = 0; i < itemCount; i++)
        {
            var productId = Guid.NewGuid();
            var sku = f.Commerce.Ean13();
            var unitPrice = Money.Create(f.Random.Decimal(10, 100), "USD");
            var quantity = f.Random.Int(1, 5);
            var discount = 0m;
            var priceBreakdown = PriceBreakdown.CreateSimple(unitPrice.Amount * quantity, "USD");
            order.AddItem(productId, sku, unitPrice, quantity, discount, priceBreakdown);
        }
        return order;
    }
}

public static class ShippingAddressFaker
{
    public static ShippingAddress Generate()
    {
        var f = new Faker();
        return ShippingAddress.Create(f.Name.FullName(), f.Address.StreetAddress(), f.Address.City(), "State", "Country", "0000", "+1234567890");
    }
}
