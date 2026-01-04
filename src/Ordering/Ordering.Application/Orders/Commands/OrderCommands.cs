using NetCommerce.SharedKernel.Application;

namespace NetCommerce.Ordering.Application.Orders.Commands;

/// <summary>
///     Command to create a new order.
/// </summary>
public record CreateOrderCommand(
    Guid CustomerId,
    List<OrderItemDto> Items,
    AddressDto ShippingAddress,
    AddressDto BillingAddress,
    string PaymentMethod) : ICommand<Guid>;

/// <summary>
///     Command to add an item to an existing order.
/// </summary>
public record AddOrderItemCommand(
    Guid OrderId,
    Guid ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    string Currency) : ICommand;

/// <summary>
///     Command to cancel an order.
/// </summary>
public record CancelOrderCommand(
    Guid OrderId,
    string Reason) : ICommand;

/// <summary>
///     Command to confirm an order (after payment).
/// </summary>
public record ConfirmOrderCommand(Guid OrderId, string PaymentTransactionId) : ICommand;

/// <summary>
///     Command to ship an order.
/// </summary>
public record ShipOrderCommand(
    Guid OrderId,
    string TrackingNumber,
    string Carrier) : ICommand;

/// <summary>
///     Command to mark order as delivered.
/// </summary>
public record DeliverOrderCommand(Guid OrderId) : ICommand;

/// <summary>
///     DTO for order items in commands.
/// </summary>
public record OrderItemDto(
    Guid ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    string Currency);

/// <summary>
///     DTO for address in commands.
/// </summary>
public record AddressDto(
    string Street,
    string City,
    string State,
    string PostalCode,
    string Country,
    string RecipientName,
    string PhoneNumber);
