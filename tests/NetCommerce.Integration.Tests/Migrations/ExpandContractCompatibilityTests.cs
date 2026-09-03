#nullable enable
using Microsoft.EntityFrameworkCore;
using NetCommerce.Catalog.Domain.Products;
using NetCommerce.Domain.Shared;
using NetCommerce.Integration.Tests.Fixtures;
using NetCommerce.Inventory.Domain.Stock;
using NetCommerce.Ordering.Domain.Orders;
using NetCommerce.Payments.Domain.Transactions;
using Npgsql;
using Shouldly;
using Xunit;

namespace NetCommerce.Integration.Tests.Migrations;

[Collection(nameof(IntegrationTestCollection))]
[Trait("Category", "BlueGreenCompatibility")]
public sealed class ExpandContractCompatibilityTests : IntegrationTestBase
{
    public ExpandContractCompatibilityTests(IntegrationTestFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task VersionN_Application_MustContinueOperating_AfterVersionNPlusOne_SchemaExpansion()
    {
        var productId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid().ToString();

        // 1. Seed baseline records
        await using (var catalogDb = Fixture.CreateCatalogDbContext())
        {
            var product = Product.Create("Mechanical Keyboard", "RGB 10-keyless", $"KB-{Guid.NewGuid():N}"[..12], Money.Create(129.99m, "GEL"), Guid.NewGuid());
            product.Publish();
            catalogDb.Products.Add(product);
            await catalogDb.SaveChangesAsync();
            productId = product.Id;
        }

        await using (var inventoryDb = Fixture.CreateInventoryDbContext())
        {
            var stock = Stock.Create(productId, "SKU-EXPAND-01", 100);
            inventoryDb.Stocks.Add(stock);
            await inventoryDb.SaveChangesAsync();
        }

        // 2. Apply N+1 Expansion DDL
        await using (var connection = new NpgsqlConnection(Fixture.PostgresConnectionString))
        {
            await connection.OpenAsync();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                ALTER TABLE catalog.products ADD COLUMN IF NOT EXISTS manufacturer_warranty_months integer NULL;
                ALTER TABLE ordering.orders ADD COLUMN IF NOT EXISTS loyalty_tier_applied text NOT NULL DEFAULT 'Standard';
                CREATE TABLE IF NOT EXISTS inventory.stock_barcodes (
                    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                    stock_id uuid NOT NULL,
                    barcode text NOT NULL UNIQUE,
                    is_active boolean NOT NULL DEFAULT true
                );
                ALTER TABLE payments.payment_transactions ADD COLUMN IF NOT EXISTS risk_score numeric(5,2) NULL;
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        try
        {
            // 3. Assert Version N domain logic continues to read and write cleanly
            await using (var catalogDb = Fixture.CreateCatalogDbContext())
            {
                var queriedProduct = await catalogDb.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId);
                queriedProduct.ShouldNotBeNull();
                queriedProduct.Price.Amount.ShouldBe(129.99m);
            }

            await using (var orderingDb = Fixture.CreateOrderingDbContext())
            {
                var address = ShippingAddress.Create("John Doe", "123 Rustaveli Ave", "Tbilisi", "Tbilisi", "GE", "0108", "+995555112233");
                var order = Order.Create(customerId, address, idempotencyKey);
                orderId = order.Id;

                var breakdown = PriceBreakdown.Create(129.99m, 0m, 23.40m, 0.18m, "VAT", "GEL");
                order.AddItem(productId, "Mechanical Keyboard", Money.Create(129.99m, "GEL"), 1, 1.2m, breakdown, "SKU-EXPAND-01");

                orderingDb.Orders.Add(order);
                await Should.NotThrowAsync(async () => await orderingDb.SaveChangesAsync());
            }

            await using (var orderingDb = Fixture.CreateOrderingDbContext())
            {
                var order = await orderingDb.Orders.FirstAsync(o => o.Id == orderId);
                order.ConfirmGracePeriod();
                await Should.NotThrowAsync(async () => await orderingDb.SaveChangesAsync());
                order.Status.ShouldBe(OrderStatus.AwaitingValidation);
            }

            await using (var paymentsDb = Fixture.CreatePaymentsDbContext())
            {
                var payment = PaymentTransaction.Create(orderId, Money.Create(129.99m, "GEL"), PaymentProvider.Stripe, $"idemp_{orderId:N}");
                payment.SetExternalTransactionId("pi_live_test_expansion");
                payment.MarkAsCompleted("ch_expand_proof");

                paymentsDb.Transactions.Add(payment);
                await Should.NotThrowAsync(async () => await paymentsDb.SaveChangesAsync());
            }

            // Verify the server default took effect for Version N write
            await using (var connection = new NpgsqlConnection(Fixture.PostgresConnectionString))
            {
                await connection.OpenAsync();
                await using var verifyCmd = connection.CreateCommand();
                verifyCmd.CommandText = "SELECT loyalty_tier_applied FROM ordering.orders WHERE id = @id;";
                verifyCmd.Parameters.AddWithValue("id", orderId);

                var defaultVal = await verifyCmd.ExecuteScalarAsync();
                defaultVal.ShouldBe("Standard");
            }
        }
        finally
        {
            // Guaranteed cleanup prevents test collection schema drift
            await using var connection = new NpgsqlConnection(Fixture.PostgresConnectionString);
            await connection.OpenAsync();
            await using var cleanupCmd = connection.CreateCommand();
            cleanupCmd.CommandText = @"
                ALTER TABLE catalog.products DROP COLUMN IF EXISTS manufacturer_warranty_months;
                ALTER TABLE ordering.orders DROP COLUMN IF EXISTS loyalty_tier_applied;
                DROP TABLE IF EXISTS inventory.stock_barcodes;
                ALTER TABLE payments.payment_transactions DROP COLUMN IF EXISTS risk_score;
            ";
            await cleanupCmd.ExecuteNonQueryAsync();
        }
    }
}
