#nullable enable
using Asp.Versioning.Builder; // Required for ApiVersionSet

namespace NetCommerce.Api.Endpoints;

/// <summary>
///     Interface for defining endpoint groups in Minimal API.
/// </summary>
public interface IEndpointGroup
{
    // Added ApiVersionSet parameter
    void MapEndpoints(IEndpointRouteBuilder app, ApiVersionSet versionSet);
}

/// <summary>
///     Extension methods for registering endpoint groups.
/// </summary>
public static class EndpointExtensions
{
    public static IEndpointRouteBuilder MapEndpointGroups(this IEndpointRouteBuilder app, ApiVersionSet versionSet)
    {
        var endpointGroupType = typeof(IEndpointGroup);
        var assembly = typeof(IEndpointGroup).Assembly;

        var endpointGroups = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.IsAssignableTo(endpointGroupType))
            .Select(Activator.CreateInstance)
            .Cast<IEndpointGroup>();

        foreach (var group in endpointGroups)
        {
            group.MapEndpoints(app, versionSet); // Pass it down
        }

        return app;
    }

    public static IEndpointRouteBuilder MapAllEndpoints(this IEndpointRouteBuilder app, ApiVersionSet versionSet)
    {
        var endpointTypes = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(s => s.GetTypes())
            .Where(t => t.IsAssignableTo(typeof(IEndpoint)) && t is { IsInterface: false, IsAbstract: false });

        foreach (var endpointType in endpointTypes)
        {
            // ვიყენებთ static abstract მეთოდს
            endpointType.GetMethod(nameof(IEndpoint.Map))?.Invoke(null, [app, versionSet]);
        }

        return app;
    }
}
