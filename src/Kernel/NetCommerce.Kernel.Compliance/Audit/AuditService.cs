#nullable enable
using System.Text.Json;
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
        var contextJson = JsonSerializer.Serialize(command, commandType,
            new JsonSerializerOptions { WriteIndented = false, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        var auditEntry = AuditEntry.Create(
            _userContext.UserId,
            _userContext.Role,
            $"{command.Module}.{actionName}",
            command.GetResourceId(),
            command.Module,
            contextJson,
            correlationId ?? Guid.NewGuid().ToString(),
            _userContext.IpAddress,
            _userContext.UserAgent
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
            ? JsonSerializer.Serialize(context, new JsonSerializerOptions { WriteIndented = false, PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
            : "{}";

        var auditEntry = AuditEntry.Create(
            _userContext.UserId,
            _userContext.Role,
            action,
            resourceId,
            module,
            contextJson,
            correlationId ?? Guid.NewGuid().ToString(),
            _userContext.IpAddress,
            _userContext.UserAgent
        );

        await _auditRepository.StoreAsync(auditEntry, cancellationToken);
    }
}
