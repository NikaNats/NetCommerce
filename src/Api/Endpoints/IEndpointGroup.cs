#nullable enable
using Asp.Versioning.Builder;

namespace NetCommerce.Api.Endpoints;

/// <summary>
///     Interface for defining endpoint groups in Minimal API.
///     Use explicit registration in Program.cs for Native AOT compatibility.
/// </summary>
public interface IEndpointGroup
{
    void MapEndpoints(IEndpointRouteBuilder app, ApiVersionSet versionSet);
}
