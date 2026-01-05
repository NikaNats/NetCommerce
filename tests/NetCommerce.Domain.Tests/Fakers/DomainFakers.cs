using Bogus;
using NetCommerce.Catalog.Domain.Products;
using NetCommerce.Inventory.Domain.Stock;
using NetCommerce.Ordering.Domain.Orders;
using NetCommerce.SharedKernel.Domain;

namespace NetCommerce.Domain.Tests.Fakers;

/// <summary>
///     Bogus faker for Money value object.
/// </summary>
public static class MoneyFaker
{
    public static Money Generate()
    {
        var faker = new Faker();
        return Money.Create(
            faker.Finance.Amount(1, 10000),
            faker.PickRandom("GEL", "USD", "EUR"));
    }

    public static Money Generate(decimal amount, string currency = "GEL")
    {
        return Money.Create(amount, currency);
    }
}

/// <summary>
///     Bogus faker for Product aggregate.
/// </summary>
public static class ProductFaker
{
    public static Product Generate()
    {
        var faker = new Faker();
        return Product.Create(
            faker.Commerce.ProductName(),
            faker.Commerce.ProductDescription(),
            faker.Commerce.Ean13(),
            MoneyFaker.Generate(),
            Guid.NewGuid());
    }
}

/// <summary>
///     Bogus faker for ShippingAddress value object.
/// </summary>
public static class ShippingAddressFaker
{
    public static ShippingAddress Generate()
    {
        var faker = new Faker();
        return ShippingAddress.Create(
            faker.Name.FullName(),
            faker.Address.StreetAddress(),
            faker.Address.City(),
            faker.Address.State(),
            faker.Address.Country(),
            faker.Address.ZipCode(),
            faker.Phone.PhoneNumber());
    }
}

/// <summary>
///     Bogus faker for Order aggregate.
/// </summary>
public static class OrderFaker
{
    public static Order Generate()
    {
        var faker = new Faker();
        return Order.Create(
            Guid.NewGuid(),
            ShippingAddressFaker.Generate(),
            faker.Random.Guid().ToString(),
            faker.Lorem.Sentence());
    }

    public static Order GenerateWithItems(int itemCount = 3, string currency = "GEL")
    {
        var order = Generate();
        var faker = new Faker();

        for (var i = 0; i < itemCount; i++)
        {
            var basePrice = MoneyFaker.Generate(faker.Finance.Amount(10), currency);
            var breakdown = PriceBreakdown.Create(basePrice.Amount, 0, 0, 0, "None", currency);
            order.AddItem(
                Guid.NewGuid(),
                faker.Commerce.ProductName(),
                basePrice,
                faker.Random.Int(1, 5),
                2.0m,
                breakdown);
        }

        return order;
    }
}

/// <summary>
///     Bogus faker for Stock aggregate.
/// </summary>
public static class StockFaker
{
    public static Stock Generate()
    {
        var faker = new Faker();
        return Stock.Create(
            Guid.NewGuid(),
            faker.Commerce.Ean13(),
            faker.Random.Int(10, 1000),
            faker.Random.Int(5, 20),
            faker.Address.City() + " Warehouse");
    }

    public static Stock Generate(int quantity, int threshold = 10)
    {
        var faker = new Faker();
        return Stock.Create(Guid.NewGuid(), faker.Commerce.Ean13(), quantity, threshold);
    }
}
