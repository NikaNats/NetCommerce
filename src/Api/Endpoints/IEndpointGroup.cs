namespace NetCommerce.Api.Endpoints;

/// <summary>
///     Interface for defining endpoint groups in Minimal API.
/// </summary>
public interface IEndpointGroup
{
    void MapEndpoints(IEndpointRouteBuilder app);
}

/// <summary>
///     Extension methods for registering endpoint groups.
/// </summary>
public static class EndpointExtensions
{
    public static IEndpointRouteBuilder MapEndpointGroups(this IEndpointRouteBuilder app)
    {
        var endpointGroupType = typeof(IEndpointGroup);
        var assembly = typeof(IEndpointGroup).Assembly;

        var endpointGroups = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.IsAssignableTo(endpointGroupType))
            .Select(Activator.CreateInstance)
            .Cast<IEndpointGroup>();

        foreach (var group in endpointGroups) group.MapEndpoints(app);

        return app;
    }
}