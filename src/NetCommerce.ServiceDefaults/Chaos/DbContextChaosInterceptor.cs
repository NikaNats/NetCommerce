#nullable enable
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NetCommerce.ServiceDefaults.Chaos;

/// <summary>
///     EF Core interceptor that injects chaos (latency/faults) into database calls.
///
///     <para>
///     <b>Purpose:</b> Validates that the API doesn't hang when Catalog-to-DB calls are slow.
///     This implements the "verify that the API doesn't hang" requirement from Phase 7.
///     </para>
///
///     <para>
///     <b>Usage:</b> Register via AddDbContextChaosInterceptor() extension method.
///     Configure via "Chaos:Database" section in appsettings.json.
///     </para>
///
///     <para>
///     <b>CRITICAL:</b> Only enabled in non-production environments!
///     </para>
/// </summary>
public sealed class DbContextChaosInterceptor : DbCommandInterceptor
{
    private readonly ILogger<DbContextChaosInterceptor>? _logger;
    private readonly DbChaosOptions _options;
    private readonly Random _random = new();

    public DbContextChaosInterceptor(DbChaosOptions options, ILogger<DbContextChaosInterceptor>? logger = null)
    {
        _options = options;
        _logger = logger;
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        await InjectChaosAsync(command.CommandText, "Reader", cancellationToken);
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await InjectChaosAsync(command.CommandText, "NonQuery", cancellationToken);
        return result;
    }

    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        await InjectChaosAsync(command.CommandText, "Scalar", cancellationToken);
        return result;
    }

    private async Task InjectChaosAsync(string commandText, string operationType, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
            return;

        // Check if this command targets a specific schema we want to inject chaos into
        var shouldInject = ShouldInjectChaos(commandText);
        if (!shouldInject)
            return;

        // Latency injection
        if (_options.Latency.Enabled && _random.NextDouble() < _options.Latency.InjectionRate)
        {
            var delay = _random.Next(_options.Latency.MinDelayMs, _options.Latency.MaxDelayMs);
            _logger?.LogWarning(
                "[DbChaos] Injecting {DelayMs}ms latency into {OperationType} operation (Schema filter: {SchemaFilter})",
                delay, operationType, _options.TargetSchemaFilter ?? "any");

            await Task.Delay(delay, cancellationToken);
        }

        // Fault injection
        if (_options.Fault.Enabled && _random.NextDouble() < _options.Fault.InjectionRate)
        {
            _logger?.LogWarning(
                "[DbChaos] Injecting fault into {OperationType} operation",
                operationType);

            throw new InvalidOperationException(
                $"[DbChaos] Simulated database failure - {_options.Fault.FaultMessage}");
        }
    }

    private bool ShouldInjectChaos(string commandText)
    {
        // If no schema filter is set, inject chaos into all commands
        if (string.IsNullOrEmpty(_options.TargetSchemaFilter))
            return true;

        // Check if the command targets the specified schema
        // This allows targeting specific modules (e.g., "catalog" schema for Catalog-to-DB chaos)
        return commandText.Contains($"\"{_options.TargetSchemaFilter}\".", StringComparison.OrdinalIgnoreCase)
               || commandText.Contains($"{_options.TargetSchemaFilter}.", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
///     Configuration options for database chaos injection.
/// </summary>
public sealed class DbChaosOptions
{
    public const string SectionName = "Chaos:Database";

    /// <summary>
    ///     Whether database chaos injection is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    ///     Optional schema filter to target specific modules.
    ///     E.g., "catalog" to only inject chaos into Catalog DB calls.
    /// </summary>
    public string? TargetSchemaFilter { get; set; }

    /// <summary>
    ///     Latency injection configuration.
    /// </summary>
    public DbLatencyOptions Latency { get; set; } = new();

    /// <summary>
    ///     Fault injection configuration.
    /// </summary>
    public DbFaultOptions Fault { get; set; } = new();
}

/// <summary>
///     Database latency injection options.
/// </summary>
public sealed class DbLatencyOptions
{
    /// <summary>
    ///     Whether latency injection is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    ///     Probability of injecting latency (0.0 to 1.0). Default: 100%
    /// </summary>
    public double InjectionRate { get; set; } = 1.0;

    /// <summary>
    ///     Minimum delay in milliseconds. Default: 500ms as per Phase 7 requirement.
    /// </summary>
    public int MinDelayMs { get; set; } = 500;

    /// <summary>
    ///     Maximum delay in milliseconds. Default: 500ms (fixed latency).
    /// </summary>
    public int MaxDelayMs { get; set; } = 500;
}

/// <summary>
///     Database fault injection options.
/// </summary>
public sealed class DbFaultOptions
{
    /// <summary>
    ///     Whether fault injection is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    ///     Probability of injecting a fault (0.0 to 1.0). Default: 5%
    /// </summary>
    public double InjectionRate { get; set; } = 0.05;

    /// <summary>
    ///     Custom fault message.
    /// </summary>
    public string FaultMessage { get; set; } = "Chaos monkey database fault";
}

/// <summary>
///     Extension methods for registering the DB chaos interceptor.
/// </summary>
public static class DbChaosExtensions
{
    /// <summary>
    ///     Adds the DbContext chaos interceptor for controlled failure injection.
    ///
    ///     <para>
    ///     Configuration example (appsettings.Development.json):
    ///     <code>
    ///     {
    ///       "Chaos": {
    ///         "Database": {
    ///           "Enabled": true,
    ///           "TargetSchemaFilter": "catalog",
    ///           "Latency": {
    ///             "Enabled": true,
    ///             "InjectionRate": 1.0,
    ///             "MinDelayMs": 500,
    ///             "MaxDelayMs": 500
    ///           }
    ///         }
    ///       }
    ///     }
    ///     </code>
    ///     </para>
    /// </summary>
    public static IServiceCollection AddDbContextChaosInterceptor(
        this IServiceCollection services,
        IHostEnvironment environment,
        Action<DbChaosOptions>? configure = null)
    {
        // CRITICAL: Never enable in production!
        if (environment.IsProduction())
        {
            return services;
        }

        var options = new DbChaosOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<DbContextChaosInterceptor>();

        return services;
    }
}
