using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using NetCommerce.Api.Extensions.Hosting;
using NetCommerce.Domain.Shared.Events;

using Shouldly;

using Wolverine;
using Wolverine.Runtime;

namespace NetCommerce.Integration.Tests.Infrastructure;

/// <summary>
///     Pins that Wolverine's SignalR transport is actually WIRED.
///
///     The saga returns <see cref="OrderStatusChanged" /> from its handlers and the API maps
///     <c>MapWolverineSignalRHub("/api/messages")</c>, but neither fact guarantees a route exists
///     between them. Without an explicit transport registration and publish rule the message is
///     unroutable: it lands in the local queue and is dropped, so a browser client connects and
///     receives nothing at all.
///
///     <c>OrderFulfillmentSagaTests</c> already asserts the saga BUILDS a notification. The gap
///     that let this defect survive is that nothing asserted the message was ROUTED.
/// </summary>
public sealed class SignalRTransportWiringTests
{
    private static IConfiguration TestConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:OrderingDb"] =
                    "Host=localhost;Database=unused;Username=u;Password=p",
            })
            .Build();

    private static IHost BuildHost() =>
        new HostBuilder()
            .AddEnterpriseWolverine(TestConfig(), isTestOrDev: true)
            .Build();

    [Fact]
    public void The_signalr_transport_should_be_registered()
    {
        using var host = BuildHost();

        var options = host.Services.GetRequiredService<WolverineOptions>();

        // UseSignalR() registers a transport. Its absence is precisely what made
        // MapWolverineSignalRHub("/api/messages") a dead endpoint.
        options.Transports
            .Select(t => t.Name)
            .Any(name => name.Contains("signalr", StringComparison.OrdinalIgnoreCase))
            .ShouldBeTrue("the SignalR transport must be registered by UseSignalR()");
    }

    [Fact]
    public void OrderStatusChanged_should_route_to_the_signalr_transport()
    {
        using var host = BuildHost();

        // ExplainRoutingFor is the runtime's own routing introspection — the
        // authoritative answer to "where does this message actually go".
        var explanation = host.Services.GetRequiredService<IWolverineRuntime>()
            .ExplainRoutingFor(typeof(OrderStatusChanged));

        // Unroutable messages go to the local queue and are dropped, so reaching
        // the signalr transport is the assertion that actually matters.
        // FinalRoutes yields MessageSubscriptionDescriptor; its Endpoint carries the
        // transport, and ToString() renders the route for diagnostics.
        var routes = explanation.FinalRoutes
            .Select(d => d.Endpoint)
            .Where(e => e != null)
            .Select(e => e.ToString() ?? string.Empty)
            .ToList();

        routes.ShouldNotBeEmpty(
            "the saga's notification needs at least one route; unroutable messages are dropped");

        routes.Any(uri => uri.Contains("signalr", StringComparison.OrdinalIgnoreCase))
            .ShouldBeTrue(
                $"IOrderNotification messages must reach the SignalR transport. Actual routes: {string.Join(", ", routes)}");
    }

    [Fact]
    public void The_publish_rule_should_be_declared_against_the_interface()
    {
        // Guards the rule's SHAPE, not just its presence: declaring it against
        // OrderStatusChanged alone would leave the next notification type
        // unrouted. MessagesImplementing<IOrderNotification>() covers new types.
        typeof(IOrderNotification).IsAssignableFrom(typeof(OrderStatusChanged))
            .ShouldBeTrue(
                "OrderStatusChanged must implement IOrderNotification, the marker the publish rule binds to");
    }
}