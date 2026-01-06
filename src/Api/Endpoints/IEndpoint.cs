using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Builder;
using Asp.Versioning.Builder;

namespace NetCommerce.Api.Endpoints;

public interface IEndpoint
{
    // Pass the versionSet here so every slice can opt-in to versioning
    static abstract void Map(IEndpointRouteBuilder app, ApiVersionSet versionSet);
}
