namespace NetCommerce.Api.Endpoints.Common;

/// <summary>
///     Order-id plus human message, shared by the customer cancel flow and the
///     admin recovery flow (identical wire shape). Named record because anonymous
///     payloads crash the AOT source-generation serializer (see CreatedResponse).
/// </summary>
public sealed record OrderMessageResponse(Guid OrderId, string Message);
