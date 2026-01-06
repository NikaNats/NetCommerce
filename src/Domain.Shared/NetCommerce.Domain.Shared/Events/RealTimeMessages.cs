#nullable enable
using Wolverine;

namespace NetCommerce.Domain.Shared.Events;

/// <summary>
///     Marker interface for real-time order notifications.
///     Messages implementing this interface are automatically routed to SignalR.
/// </summary>
public interface IOrderNotification : WebSocketMessage;

/// <summary>
///     Real-time notification sent to the browser when order status changes.
///     Pushed via SignalR WebSocket connection.
/// </summary>
/// <param name="OrderId">The order identifier</param>
/// <param name="Status">Status category: "Processing", "Success", "Error"</param>
/// <param name="Message">Human-readable message for the user</param>
public record OrderStatusChanged(
    Guid OrderId,
    string Status,
    string Message) : IOrderNotification;
