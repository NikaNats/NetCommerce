#nullable enable

using Microsoft.Extensions.Logging;
using NetCommerce.Domain.Shared.Events;
using NetCommerce.Kernel.Core.Results;
using NetCommerce.Shipping.Application.Adapters;
using NetCommerce.Shipping.Application.Services;
using NetCommerce.Shipping.Domain;

namespace NetCommerce.Shipping.Infrastructure.Services;

/// <summary>
///     Implementation of the shipping service.
///     Orchestrates courier adapters and manages shipment creation.
/// </summary>
public sealed class ShippingService : IShippingService
{
    private readonly Dictionary<string, ICourierAdapter> _courierAdapters;
    private readonly ILogger<ShippingService> _logger;
    // TODO: Add IShipmentRepository for persistence

    public ShippingService(
        IEnumerable<ICourierAdapter> courierAdapters,
        ILogger<ShippingService> logger)
    {
        _courierAdapters = courierAdapters.ToDictionary(a => a.CourierName, StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    public async Task<Result<ShippingLabelDto>> CreateLabelAsync(
        Guid orderId,
        string orderNumber,
        ShippingAddressDto addressDto,
        IReadOnlyList<ShippingItemDto> items,
        string? preferredCourier = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Creating shipping label for Order {OrderId} with courier {Courier}",
            orderId,
            preferredCourier ?? "auto-select");

        // Select courier adapter
        var adapter = SelectCourier(preferredCourier);
        if (adapter == null)
        {
            return Result.Failure<ShippingLabelDto>(
                new Error("Shipping.CourierNotAvailable", $"Courier '{preferredCourier}' not available"));
        }

        try
        {
            // Calculate total weight
            var totalWeight = items.Sum(i => i.WeightKg * i.Quantity);

            // Estimate dimensions (mock logic - in production, use product catalog)
            var dimensions = new ShipmentDimensions(30, 20, 15);

            // Map DTO to domain Address
            var address = new Address(
                addressDto.RecipientName,
                addressDto.Street,
                addressDto.City,
                addressDto.State,
                addressDto.Country,
                addressDto.PostalCode,
                addressDto.Phone);

            // Call courier API
            var labelResult = await adapter.CreateLabelAsync(
                address,
                totalWeight,
                dimensions,
                cancellationToken);

            // Create shipment entity
            var shipment = Shipment.Create(
                orderId,
                labelResult.TrackingNumber,
                adapter.CourierName,
                address,
                totalWeight,
                dimensions,
                labelResult.EstimatedDeliveryDate);

            // TODO: Persist shipment to database via repository
            // await _shipmentRepository.AddAsync(shipment, cancellationToken);

            _logger.LogInformation(
                "Shipment {ShipmentId} created for Order {OrderId} with tracking {TrackingNumber}",
                shipment.Id,
                orderId,
                labelResult.TrackingNumber);

            return Result<ShippingLabelDto>.Success(new ShippingLabelDto(
                shipment.Id,
                labelResult.TrackingNumber,
                adapter.CourierName,
                labelResult.LabelUrl,
                labelResult.ShippingCost,
                labelResult.EstimatedDeliveryDate));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create shipping label for Order {OrderId} with courier {Courier}",
                orderId,
                adapter.CourierName);

            return Result.Failure<ShippingLabelDto>(
                new Error("Shipping.LabelCreationFailed", $"Failed to create shipping label: {ex.Message}"));
        }
    }

    private ICourierAdapter? SelectCourier(string? preferredCourier)
    {
        if (!string.IsNullOrWhiteSpace(preferredCourier))
        {
            return _courierAdapters.GetValueOrDefault(preferredCourier);
        }

        // Default to first available courier (in production, use smart routing)
        return _courierAdapters.Values.FirstOrDefault();
    }
}
