namespace NetCommerce.Api.Endpoints.Common;

/// <summary>
///     Standard body for 201 Created responses carrying the new resource id.
///     Serializes as {"id":"..."} under the API's camelCase policy, identical
///     to the anonymous type it replaces.
/// </summary>
/// <remarks>
///     A named record is REQUIRED here, not a convenience: the API serializes
///     through a source-generation-only JsonSerializerContext (Native AOT),
///     which has no metadata for compiler-generated anonymous types. Returning
///     <c>Results.Created(location, new { id = ... })</c> throws
///     "JsonTypeInfo metadata ... was not provided" at runtime — a 500 on every
///     create endpoint. Proven live: POST /api/v1/categories 500'd until this
///     record replaced the anonymous payload.
/// </remarks>
public sealed record CreatedResponse(Guid Id);
