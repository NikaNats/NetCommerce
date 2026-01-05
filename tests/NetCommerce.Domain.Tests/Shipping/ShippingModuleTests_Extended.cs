// FILE: tests/NetCommerce.Domain.Tests/Shipping/ShippingModuleTests_Extended.cs

using Microsoft.Extensions.Logging;
using NetCommerce.SharedKernel.Events;
using NetCommerce.Shipping.Application.Adapters;
using NetCommerce.Shipping.Application.Services;
using NetCommerce.Shipping.Domain;
using NetCommerce.Shipping.Infrastructure.Adapters;
using NetCommerce.Shipping.Infrastructure.Services;
using NSubstitute;
using Shouldly;

namespace NetCommerce.Domain.Tests.Shipping;

public class ShippingModuleTests_Extended
{
    private readonly ILogger<DhlCourierAdapter> _dhlLogger = Substitute.For<ILogger<DhlCourierAdapter>>();
    private readonly ILogger<ShippingService> _serviceLogger = Substitute.For<ILogger<ShippingService>>();

    [Fact]
    public async Task CourierAdapter_ShouldCalculateInternationalSurcharge()
    {
        // Arrange
        var adapter = new DhlCourierAdapter(_dhlLogger);
        var domestic = new Address("Name", "St", "City", "State", "US", "00000", "Ph");
        var international = new Address("Name", "St", "Tbilisi", "Tbilisi", "GE", "0100", "Ph"); // Georgia
        var dim = new ShipmentDimensions(10, 10, 10);

        // Act
        var domesticResult = await adapter.CreateLabelAsync(domestic, 1m, dim);
        var intlResult = await adapter.CreateLabelAsync(international, 1m, dim);

        // Assert
        intlResult.ShippingCost.ShouldBeGreaterThan(domesticResult.ShippingCost);
    }

    [Fact]
    public async Task ShippingService_ShouldCalculateTotalWeight_FromMultipleItems()
    {
        // Arrange
        var adapter = Substitute.For<ICourierAdapter>();
        adapter.CourierName.Returns("DHL");

        decimal capturedWeight = 0;
        adapter.CreateLabelAsync(Arg.Any<Address>(), Arg.Do<decimal>(w => capturedWeight = w), Arg.Any<ShipmentDimensions>(), default)
            .Returns(new CourierLabelResult("TRK", "url", 10m, "USD", DateTime.UtcNow));

        var service = new ShippingService([adapter], _serviceLogger);

        var items = new List<ShippingItemDto>
        {
            new(Guid.NewGuid(), "Item 1", 2, 1.5m), // 2 * 1.5 = 3.0kg
            new(Guid.NewGuid(), "Item 2", 3, 0.5m)  // 3 * 0.5 = 1.5kg
        };
        // Total = 4.5kg

        // Act
        await service.CreateLabelAsync(Guid.NewGuid(), "ORD-1", CreateAddressDto(), items, "DHL");

        // Assert
        capturedWeight.ShouldBe(4.5m);
    }

    [Fact]
    public void Shipment_StateTransitions_ShouldEnforceRules()
    {
        // Arrange
        var shipment = Shipment.Create(Guid.NewGuid(), "TRK", "DHL", CreateAddress(), 1m, new ShipmentDimensions(1,1,1), DateTime.UtcNow);

        // Act & Assert 1: LabelCreated -> InTransit
        shipment.MarkPickedUp();
        shipment.Status.ShouldBe(ShipmentStatus.InTransit);
        shipment.PickedUpAt.ShouldNotBeNull();

        // Act & Assert 2: InTransit -> Delivered
        shipment.MarkDelivered();
        shipment.Status.ShouldBe(ShipmentStatus.Delivered);
        shipment.DeliveredAt.ShouldNotBeNull();
    }

    [Fact]
    public void Shipment_InvalidTransition_ShouldThrow()
    {
        // Arrange
        var shipment = Shipment.Create(Guid.NewGuid(), "TRK", "DHL", CreateAddress(), 1m, new ShipmentDimensions(1,1,1), DateTime.UtcNow);

        // Act & Assert: Cannot go straight from LabelCreated -> Delivered without Pickup
        Should.Throw<InvalidOperationException>(() => shipment.MarkDelivered())
            .Message.ShouldContain("LabelCreated");
    }

    private Address CreateAddress() => new("Name", "St", "City", "State", "Country", "00000", "555-1234");
    private ShippingAddressDto CreateAddressDto() => new("Name", "St", "City", "State", "Country", "00000", "555-1234");
}
