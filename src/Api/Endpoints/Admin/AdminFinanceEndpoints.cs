using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NetCommerce.Finance.Application.Commands;
using NetCommerce.Finance.Domain.Reconciliation;
using Wolverine;

namespace NetCommerce.Api.Endpoints.Admin;

/// <summary>
///     Admin endpoints for Financial Reconciliation management.
///     Human-in-the-loop corrections for discrepancies.
/// </summary>
[ApiController]
[Route("api/admin/finance")]
[Authorize(Roles = "Admin,Finance")]
public class AdminFinanceEndpoints : ControllerBase
{
    private readonly IMessageBus _bus;
    private readonly IReconciliationSessionRepository _sessionRepo;
    private readonly ILogger<AdminFinanceEndpoints> _logger;

    public AdminFinanceEndpoints(
        IMessageBus bus,
        IReconciliationSessionRepository sessionRepo,
        ILogger<AdminFinanceEndpoints> logger)
    {
        _bus = bus;
        _sessionRepo = sessionRepo;
        _logger = logger;
    }

    /// <summary>
    ///     Get reconciliation sessions with optional filtering.
    /// </summary>
    [HttpGet("reconciliation-sessions")]
    [ProducesResponseType(typeof(IEnumerable<ReconciliationSession>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetReconciliationSessions(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] ReconciliationStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var sessions = await _sessionRepo.GetSessionsInDateRangeAsync(
            startDate ?? DateTime.UtcNow.AddDays(-30),
            endDate ?? DateTime.UtcNow);

        var filtered = sessions.AsQueryable();

        if (status.HasValue)
        {
            filtered = filtered.Where(s => s.Status == status.Value);
        }

        var result = filtered
            .OrderByDescending(s => s.CalculatedForDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Ok(result);
    }

    /// <summary>
    ///     Get a specific reconciliation session with details.
    /// </summary>
    [HttpGet("reconciliation-sessions/{sessionId:guid}")]
    [ProducesResponseType(typeof(ReconciliationSession), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReconciliationSession([FromRoute] Guid sessionId)
    {
        var session = await _sessionRepo.GetByIdAsync(sessionId);
        if (session == null)
        {
            return NotFound();
        }

        return Ok(session);
    }

    /// <summary>
    ///     Manually trigger reconciliation for a specific date.
    /// </summary>
    [HttpPost("reconciliation-sessions/trigger")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> TriggerReconciliation([FromBody] TriggerReconciliationRequest request)
    {
        _logger.LogInformation("Manual reconciliation trigger for {Date} by {User}",
            request.Date.ToShortDateString(), User.Identity?.Name);

        var command = new CheckDailyReconciliation(request.Date);
        await _bus.PublishAsync(command);

        return Accepted(new
        {
            Message = $"Reconciliation started for {request.Date.ToShortDateString()}",
            RequestedBy = User.Identity?.Name,
            RequestId = Guid.NewGuid()
        });
    }

    /// <summary>
    ///     Resolve a financial discrepancy manually.
    ///     Critical for handling ghost charges and other mismatches.
    /// </summary>
    [HttpPost("discrepancies/resolve")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResolveDiscrepancy([FromBody] ResolveDiscrepancyRequest request)
    {
        _logger.LogWarning(
            "MANUAL DISCREPANCY RESOLUTION: Session={SessionId}, Txn={TxnId}, Action={Action}, User={User}",
            request.SessionId, request.ExternalTxnId, request.Action, User.Identity?.Name);

        var command = new ResolveDiscrepancyCommand(
            request.SessionId,
            request.ExternalTxnId,
            request.Action,
            request.Reason,
            User.Identity?.Name ?? "Unknown");

        await _bus.PublishAsync(command);

        return Accepted(new
        {
            Message = $"Discrepancy resolution initiated: {request.Action}",
            SessionId = request.SessionId,
            ExternalTxnId = request.ExternalTxnId,
            ProcessedBy = User.Identity?.Name
        });
    }

    /// <summary>
    ///     Get mismatched sessions requiring attention.
    /// </summary>
    [HttpGet("alerts/mismatched-sessions")]
    [ProducesResponseType(typeof(IEnumerable<ReconciliationSession>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMismatchedSessions([FromQuery] DateTime? since = null)
    {
        var sessions = await _sessionRepo.GetMismatchedSessionsAsync(
            since ?? DateTime.UtcNow.AddDays(-7));

        return Ok(sessions);
    }
}

/// <summary>
///     Request model for triggering reconciliation.
/// </summary>
public record TriggerReconciliationRequest(DateTime Date);

/// <summary>
///     Request model for resolving discrepancies.
/// </summary>
public record ResolveDiscrepancyRequest(
    Guid SessionId,
    string ExternalTxnId,
    DiscrepancyResolutionAction Action,
    string Reason);
