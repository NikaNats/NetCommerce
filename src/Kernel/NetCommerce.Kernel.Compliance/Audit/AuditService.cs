#nullable enable
using System.Text.Json;
using System.Security.Claims;
using NetCommerce.Kernel.Application;

namespace NetCommerce.Kernel.Compliance.Audit;

/// <summary>
///     Core audit service for creating and storing audit entries.
///     Decoupled from messaging infrastructure - can be used with any framework.
/// </summary>
public class AuditService
{
    private readonly IAuditRepository _auditRepository;
    private readonly IUserContext _userContext;

    /// <summary>
    ///     Cached serializer options (CA1869): audit serialization must not allocate
    ///     a new JsonSerializerOptions instance per call.
    /// </summary>
    private static readonly JsonSerializerOptions s_auditJsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AuditService(IAuditRepository auditRepository, IUserContext userContext)
    {
        _auditRepository = auditRepository;
        _userContext = userContext;
    }

    /// <summary>
    ///     Creates and stores an audit entry for an auditable command.
    /// </summary>
    public async Task AuditAsync(
        IAuditableCommand command,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        var actionName = command.GetType().Name
            .Replace("Command", string.Empty)
            .Replace("Query", string.Empty);

        var commandType = command.GetType();
        var contextJson = JsonSerializer.Serialize(command, commandType, s_auditJsonOptions);

        var auditEntry = AuditEntry.Create(
            _userContext.UserId,
            string.Join(",", _userContext.Roles) ?? "Unknown",
            $"{command.Module}.{actionName}",
            command.GetResourceId(),
            command.Module,
            contextJson,
            correlationId ?? Guid.NewGuid().ToString(),
            _userContext.GetClaim("ip_address"),
            _userContext.GetClaim("user_agent")
        );

        await _auditRepository.StoreAsync(auditEntry, cancellationToken);
    }

    /// <summary>
    ///     Creates and stores a custom audit entry.
    /// </summary>
    public async Task AuditAsync(
        string action,
        string resourceId,
        string module,
        object? context = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        var contextJson = context is not null
            ? JsonSerializer.Serialize(context, s_auditJsonOptions)
            : "{}";

        var auditEntry = AuditEntry.Create(
            _userContext.UserId,
            string.Join(",", _userContext.Roles) ?? "Unknown",
            action,
            resourceId,
            module,
            contextJson,
            correlationId ?? Guid.NewGuid().ToString(),
            _userContext.GetClaim("ip_address"),
            _userContext.GetClaim("user_agent")
        );

        await _auditRepository.StoreAsync(auditEntry, cancellationToken);
    }
}
