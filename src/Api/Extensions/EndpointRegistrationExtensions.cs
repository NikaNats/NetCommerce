#region

using Asp.Versioning.Builder;
using NetCommerce.Api.Endpoints.Admin;
using NetCommerce.Api.Endpoints.Auth;
using NetCommerce.Api.Endpoints.Basket;
using NetCommerce.Api.Endpoints.Catalog;
using NetCommerce.Api.Endpoints.Inventory;
using NetCommerce.Api.Endpoints.Media;
using NetCommerce.Api.Endpoints.Ordering;
using NetCommerce.Api.Endpoints.Payments;
// Import all endpoint namespaces explicitly

#endregion

namespace NetCommerce.Api.Extensions;

/// <summary>
///     AOT-compatible endpoint registration.
///     Uses explicit instantiation instead of reflection-based assembly scanning.
/// </summary>
public static class EndpointRegistrationExtensions
{
    /// <summary>
    ///     Registers all NetCommerce API endpoints explicitly.
    ///     The AOT compiler sees these "new" calls and preserves the classes in the final binary.
    /// </summary>
    public static IEndpointRouteBuilder MapNetCommerceEndpoints(
        this IEndpointRouteBuilder app,
        ApiVersionSet versionSet)
    {
        // Catalog
        new ProductEndpoints().MapEndpoints(app, versionSet);
        new CategoryEndpoints().MapEndpoints(app, versionSet);
        new SearchEndpoints().MapEndpoints(app, versionSet);

        // Ordering
        new OrderEndpoints().MapEndpoints(app, versionSet);

        // Inventory
        new InventoryEndpoints().MapEndpoints(app, versionSet);

        // Basket
        new BasketEndpoints().MapEndpoints(app, versionSet);

        // Media
        new MediaEndpoints().MapEndpoints(app, versionSet);

        // Payments - Uses IEndpoint (static abstract) instead of IEndpointGroup
        PaymentWebhookEndpoints.Map(app, versionSet);

        // Authentication - Token management, refresh rotation, session info
        new AuthEndpoints().MapEndpoints(app, versionSet);

        // Admin - Operational Recovery, Finance Management, and DLQ
        new AdminFinanceEndpoints().MapEndpoints(app, versionSet);
        new AdminOrderRecoveryEndpoints().MapEndpoints(app, versionSet);
        new AdminDlqEndpoints().MapEndpoints(app, versionSet);

        return app;
    }
}
