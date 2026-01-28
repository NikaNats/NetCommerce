#region

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCommerce.Domain.Shared.Events;
using NetCommerce.Kernel.Application;
using NetCommerce.Kernel.Core.Results;
using NetCommerce.Shipping.Application.Adapters;
using NetCommerce.Shipping.Application.Handlers;
using NetCommerce.Shipping.Application.Repositories;
using NetCommerce.Shipping.Application.Services;
using NetCommerce.Shipping.Domain;
using NetCommerce.Shipping.Infrastructure.Adapters;
using NetCommerce.Shipping.Infrastructure.Services;

#endregion

namespace NetCommerce.Domain.Tests.Shipping;

/// <summary>
///     Unit tests for the Shipping module.
///     Tests courier adapters, shipping service, and event handlers.
/// </summary>
public class ShippingModuleTests
{
    private readonly ILogger<OrderReadyForShippingHandler> _handlerLogger;
    private readonly ILogger<ShippingService> _serviceLogger;
    private readonly ILogger<DhlCourierAdapter> _dhlLogger;
    private readonly IOptions<CourierOptions> _courierOptions;
    private readonly IShipmentRepository _shipmentRepository;
    private readonly IUnitOfWork _unitOfWork;

    public ShippingModuleTests()
    {
        _serviceLogger = Substitute.For<ILogger<ShippingService>>();
        _handlerLogger = Substitute.For<ILogger<OrderReadyForShippingHandler>>();
        _dhlLogger = Substitute.For<ILogger<DhlCourierAdapter>>();
        _courierOptions = Options.Create(new CourierOptions { UseMockMode = true });
        _shipmentRepository = Substitute.For<IShipmentRepository>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
    }

    #region Courier Adapter Tests

    [Fact]
    public async Task CourierAdapter_ShouldReturnValidTrackingNumber()
    {
        // Arrange
        var adapter = new DhlCourierAdapter(_courierOptions, _dhlLogger);
        var address = new Address(
            "John Doe",
            "123 Main St",
            "Tbilisi",
            "Tbilisi",
            "Georgia",
            "0100",
            "+995 555 123456");
        var dimensions = new ShipmentDimensions(30, 20, 15);

        // Act
        CourierLabelResult? result = await adapter.CreateLabelAsync(address, 2.5m, dimensions);

        // Assert
        result.ShouldNotBeNull();
        result.TrackingNumber.ShouldNotBeNullOrEmpty();
        result.TrackingNumber.ShouldStartWith("DHL");
        result.LabelUrl.ShouldContain("dhl");
        result.ShippingCost.ShouldBeGreaterThan(0);
        result.EstimatedDeliveryDate.ShouldNotBeNull();
    }

    [Fact]
    public async Task CourierAdapter_ShouldCalculateInternationalSurcharge()
    {
        // Arrange
        var adapter = new DhlCourierAdapter(_courierOptions, _dhlLogger);
        Address domesticAddress = CreateAddress("US");
        Address internationalAddress = CreateAddress("GE");
        var dimensions = new ShipmentDimensions(30, 20, 15);
        decimal weight = 2.0m;

        // Act
        CourierLabelResult? domesticResult = await adapter.CreateLabelAsync(domesticAddress, weight, dimensions);
        CourierLabelResult? internationalResult =
            await adapter.CreateLabelAsync(internationalAddress, weight, dimensions);

        // Assert
        internationalResult.ShippingCost.ShouldBeGreaterThan(domesticResult.ShippingCost);
    }

    #endregion

    #region Shipping Service Tests

    [Fact]
    public async Task ShippingService_ShouldSelectCorrectCourier_WhenPreferenceProvided()
    {
        // Arrange
        ICourierAdapter? dhlAdapter = Substitute.For<ICourierAdapter>();
        dhlAdapter.CourierName.Returns("DHL");
        dhlAdapter.CreateLabelAsync(Arg.Any<Address>(), Arg.Any<decimal>(), Arg.Any<ShipmentDimensions>(),
                Arg.Any<CancellationToken>())
            .Returns(
                new CourierLabelResult("DHL123", "http://dhl.com/label", 25.00m, "USD", DateTime.UtcNow.AddDays(3)));

        ICourierAdapter? fedexAdapter = Substitute.For<ICourierAdapter>();
        fedexAdapter.CourierName.Returns("FedEx");

        var service = new ShippingService(
            new[] { dhlAdapter, fedexAdapter },
            _shipmentRepository,
            _unitOfWork,
            _serviceLogger);

        ShippingAddressDto addressDto = CreateShippingAddressDto();
        var items = new List<ShippingItemDto> { new(Guid.NewGuid(), "Test Product", 1, 1.5m) };

        // Act
        Result<ShippingLabelDto>? result = await service.CreateLabelAsync(
            Guid.NewGuid(),
            "ORD-123",
            addressDto,
            items,
            "DHL");

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.CourierProvider.ShouldBe("DHL");
        await dhlAdapter.Received(1).CreateLabelAsync(
            Arg.Any<Address>(),
            Arg.Any<decimal>(),
            Arg.Any<ShipmentDimensions>(),
            Arg.Any<CancellationToken>());
        await fedexAdapter.DidNotReceive().CreateLabelAsync(
            Arg.Any<Address>(),
            Arg.Any<decimal>(),
            Arg.Any<ShipmentDimensions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShippingService_ShouldReturnFailure_WhenCourierNotAvailable()
    {
        // Arrange
        var service = new ShippingService(
            new ICourierAdapter[] { },
            _shipmentRepository,
            _unitOfWork,
            _serviceLogger);

        // Act
        Result<ShippingLabelDto>? result = await service.CreateLabelAsync(
            Guid.NewGuid(),
            "ORD-123",
            CreateShippingAddressDto(),
            new List<ShippingItemDto> { new(Guid.NewGuid(), "Product", 1, 1.0m) },
            "NonExistentCourier");

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Shipping.CourierNotAvailable");
    }

    [Fact]
    public async Task ShippingService_ShouldCalculateTotalWeight_FromMultipleItems()
    {
        // Arrange
        ICourierAdapter? adapter = Substitute.For<ICourierAdapter>();
        adapter.CourierName.Returns("DHL");

        decimal capturedWeight = 0;
        adapter.CreateLabelAsync(
                Arg.Any<Address>(),
                Arg.Do<decimal>(w => capturedWeight = w),
                Arg.Any<ShipmentDimensions>(),
                Arg.Any<CancellationToken>())
            .Returns(new CourierLabelResult("TRK123", "http://label.com", 30m, "USD", null));

        var service = new ShippingService(new[] { adapter }, _shipmentRepository, _unitOfWork, _serviceLogger);

        var items = new List<ShippingItemDto>
        {
            new(Guid.NewGuid(), "Item 1", 2, 1.5m), // 2 * 1.5 = 3.0
            new(Guid.NewGuid(), "Item 2", 3, 0.5m) // 3 * 0.5 = 1.5
        };
        // Total expected weight: 4.5kg

        // Act
        await service.CreateLabelAsync(
            Guid.NewGuid(),
            "ORD-456",
            CreateShippingAddressDto(),
            items,
            "DHL");

        // Assert
        capturedWeight.ShouldBe(4.5m);
    }

    [Fact]
    public async Task ShippingService_ShouldHandleCourierApiFailure_Gracefully()
    {
        // Arrange
        ICourierAdapter? adapter = Substitute.For<ICourierAdapter>();
        adapter.CourierName.Returns("DHL");
        adapter.CreateLabelAsync(
                Arg.Any<Address>(),
                Arg.Any<decimal>(),
                Arg.Any<ShipmentDimensions>(),
                Arg.Any<CancellationToken>())
            .Returns<CourierLabelResult>(_ => throw new Exception("Courier API unavailable"));

        var service = new ShippingService(new[] { adapter }, _shipmentRepository, _unitOfWork, _serviceLogger);

        // Act
        Result<ShippingLabelDto>? result = await service.CreateLabelAsync(
            Guid.NewGuid(),
            "ORD-789",
            CreateShippingAddressDto(),
            new List<ShippingItemDto> { new(Guid.NewGuid(), "Product", 1, 1.0m) },
            "DHL");

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Shipping.LabelCreationFailed");
        result.Error.Description.ShouldContain("Courier API unavailable");
    }

    #endregion

    #region Event Handler Tests

    [Fact]
    public async Task OrderReadyForShippingHandler_ShouldCreateShipment_AndReturnIntegrationEvent()
    {
        // Arrange
        IShippingService? shippingService = Substitute.For<IShippingService>();
        var shipmentId = Guid.NewGuid();
        shippingService.CreateLabelAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<ShippingAddressDto>(),
                Arg.Any<IReadOnlyList<ShippingItemDto>>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<ShippingLabelDto>.Success(new ShippingLabelDto(
                shipmentId,
                "DHL987654321",
                "DHL",
                "http://label.com/dhl987654321",
                35.00m,
                DateTime.UtcNow.AddDays(5))));

        var handler = new OrderReadyForShippingHandler(shippingService, _handlerLogger);

        var orderId = Guid.NewGuid();
        var @event = new OrderReadyForShipping(
            orderId,
            "ORD-2026-001",
            new List<ShippingItem> { new(Guid.NewGuid(), "Product A", 2, 1.2m) },
            CreateShippingAddressDto());

        // Act
        var result = await handler.Handle(@event, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        var shipmentCreated = result.ShouldBeOfType<ShipmentCreatedIntegrationEvent>();
        shipmentCreated.OrderId.ShouldBe(orderId);
        shipmentCreated.ShipmentId.ShouldBe(shipmentId);
        shipmentCreated.TrackingNumber.ShouldBe("DHL987654321");
        shipmentCreated.CourierProvider.ShouldBe("DHL");
    }

    [Fact]
    public async Task OrderReadyForShippingHandler_ShouldReturnFailureEvent_WhenShippingServiceFails()
    {
        // Arrange
        IShippingService? shippingService = Substitute.For<IShippingService>();
        shippingService.CreateLabelAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<ShippingAddressDto>(),
                Arg.Any<IReadOnlyList<ShippingItemDto>>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Failure<ShippingLabelDto>(
                new Error("Shipping.CourierDown", "All couriers offline")));

        var handler = new OrderReadyForShippingHandler(shippingService, _handlerLogger);

        var @event = new OrderReadyForShipping(
            Guid.NewGuid(),
            "ORD-2026-002",
            new List<ShippingItem> { new(Guid.NewGuid(), "Product", 1, 1.0m) },
            CreateShippingAddressDto());

        // Act
        var result = await handler.Handle(@event, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBeOfType<ShipmentCreationFailedEvent>(); // Handler returns failure event for retry handling
    }

    #endregion

    #region Shipment Aggregate Tests

    [Fact]
    public void Shipment_Create_ShouldInitializeWithCorrectState()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        string trackingNumber = "DHL123456789";
        Address address = CreateAddress();
        var dimensions = new ShipmentDimensions(40, 30, 20);

        // Act
        var shipment = Shipment.Create(
            orderId,
            trackingNumber,
            "DHL",
            address,
            3.5m,
            dimensions,
            DateTime.UtcNow.AddDays(4));

        // Assert
        shipment.ShouldNotBeNull();
        shipment.OrderId.ShouldBe(orderId);
        shipment.TrackingNumber.ShouldBe(trackingNumber);
        shipment.CourierProvider.ShouldBe("DHL");
        shipment.Status.ShouldBe(ShipmentStatus.LabelCreated);
        shipment.WeightKg.ShouldBe(3.5m);
        shipment.Dimensions.ShouldBe(dimensions);
    }

    [Fact]
    public void Shipment_MarkPickedUp_ShouldTransitionToInTransit()
    {
        // Arrange
        Shipment shipment = CreateShipment();

        // Act
        shipment.MarkPickedUp();

        // Assert
        shipment.Status.ShouldBe(ShipmentStatus.InTransit);
        shipment.PickedUpAt.ShouldNotBeNull();
    }

    [Fact]
    public void Shipment_MarkDelivered_ShouldTransitionToDelivered()
    {
        // Arrange
        Shipment shipment = CreateShipment();
        shipment.MarkPickedUp();

        // Act
        shipment.MarkDelivered();

        // Assert
        shipment.Status.ShouldBe(ShipmentStatus.Delivered);
        shipment.DeliveredAt.ShouldNotBeNull();
    }

    [Fact]
    public void Shipment_MarkDelivered_FromLabelCreated_ShouldThrowException()
    {
        // Arrange
        Shipment shipment = CreateShipment();

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => shipment.MarkDelivered())
            .Message.ShouldContain("Cannot mark as delivered from status LabelCreated");
    }

    [Fact]
    public void Shipment_MarkFailed_ShouldSetFailureReason()
    {
        // Arrange
        Shipment shipment = CreateShipment();
        string reason = "Address not found - recipient moved";

        // Act
        shipment.MarkFailed(reason);

        // Assert
        shipment.Status.ShouldBe(ShipmentStatus.Failed);
        shipment.FailureReason.ShouldBe(reason);
    }

    #endregion

    #region Helper Methods

    private Address CreateAddress(string country = "Georgia")
    {
        return new Address(
            "Test Recipient",
            "123 Test Street",
            "Tbilisi",
            "Tbilisi",
            country,
            "0100",
            "+995 555 000000");
    }

    private ShippingAddressDto CreateShippingAddressDto()
    {
        return new ShippingAddressDto(
            "Test Recipient",
            "123 Test Street",
            "Tbilisi",
            "Tbilisi",
            "Georgia",
            "0100",
            "+995 555 000000");
    }

    private Shipment CreateShipment()
    {
        return Shipment.Create(
            Guid.NewGuid(),
            "TEST-" + Guid.NewGuid().ToString("N")[..10],
            "DHL",
            CreateAddress(),
            2.0m,
            new ShipmentDimensions(30, 20, 15),
            DateTime.UtcNow.AddDays(3));
    }

    #endregion
}
