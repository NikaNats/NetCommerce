using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace NetCommerce.Kernel.Stripe;

/// <summary>
///     Extension methods for registering shared Stripe infrastructure.
/// </summary>
public static class StripeKernelExtensions
{
    /// <summary>
    ///     Adds shared Stripe infrastructure to the service collection.
    ///     This configures the StripeClientFactory that can be injected into
    ///     both Payments and Finance modules for consistent Stripe access.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddStripeKernel(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind Stripe options from configuration
        services.Configure<StripeOptions>(configuration.GetSection(StripeOptions.SectionName));

        // Register shared StripeClientFactory as singleton (thread-safe, configured once)
        services.AddSingleton<StripeClientFactory>();

        return services;
    }

    /// <summary>
    ///     Adds a named HttpClient with standard resilience for Stripe API calls.
    ///     Use this when you need raw HTTP access to Stripe APIs not covered by the SDK.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="clientName">Optional custom name for the HttpClient.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddStripeHttpClient(
        this IServiceCollection services,
        string clientName = "StripeHttpClient")
    {
        services.AddHttpClient(clientName, client =>
            {
                client.BaseAddress = new Uri("https://api.stripe.com/v1/");
                client.DefaultRequestHeaders.Add("Accept", "application/json");
            })
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 3;
                options.Retry.Delay = TimeSpan.FromMilliseconds(500);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(10);
                options.CircuitBreaker.FailureRatio = 0.5;
                options.CircuitBreaker.MinimumThroughput = 5;
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(120);
            });

        return services;
    }
}
