#nullable enable
using System.Diagnostics;

namespace NetCommerce.Kernel.Core.Diagnostics;

/// <summary>
///     Centralized diagnostics and tracing for NetCommerce Kernel.
///     Provides ActivitySource for OpenTelemetry distributed tracing.
/// </summary>
public static class KernelDiagnostics
{
    /// <summary>
    ///     Activity source name for NetCommerce Kernel operations.
    /// </summary>
    public const string ActivitySourceName = "NetCommerce.Kernel";

    /// <summary>
    ///     Kernel version for telemetry.
    /// </summary>
    public const string Version = "1.0.0";

    /// <summary>
    ///     ActivitySource for creating spans/traces.
    ///     Register in OpenTelemetry: tracerProvider.AddSource(KernelDiagnostics.ActivitySourceName)
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, Version);

    /// <summary>
    ///     Creates a new activity (span) for tracing operations.
    /// </summary>
    /// <param name="operationName">Name of the operation (e.g., "Repository.GetById", "Encryption.Encrypt")</param>
    /// <param name="kind">Activity kind (default: Internal)</param>
    /// <returns>Activity or null if tracing is disabled</returns>
    public static Activity? StartActivity(string operationName, ActivityKind kind = ActivityKind.Internal)
    {
        return ActivitySource.StartActivity(operationName, kind);
    }

    /// <summary>
    ///     Tag keys for standardized telemetry.
    /// </summary>
    public static class Tags
    {
        public const string EntityType = "kernel.entity.type";
        public const string EntityId = "kernel.entity.id";
        public const string RepositoryOperation = "kernel.repository.operation";
        public const string EncryptionType = "kernel.encryption.type";
        public const string AuditAction = "kernel.audit.action";
        public const string DomainEvent = "kernel.domain_event.type";
        public const string UserId = "kernel.user.id";
        public const string TenantId = "kernel.tenant.id";
    }
}
