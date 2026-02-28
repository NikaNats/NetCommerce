using Asp.Versioning.Builder;
using Microsoft.AspNetCore.Mvc;
using NetCommerce.Kernel.Wolverine.DeadLetters;

namespace NetCommerce.Api.Endpoints.Admin;

/// <summary>
///     Admin endpoints for inspecting, replaying, and dismissing Wolverine dead-lettered messages.
///
///     <para>
///     <b>Replay strategy:</b> marking a message as replayable causes Wolverine's built-in
///     durability agent to re-enqueue it on its next scan — no manual re-publish required.
///     </para>
///
///     <para>
///     <b>When to use:</b>
///     - Saga compensation commands that failed permanently and landed in the DLQ
///       (e.g., ReleaseInventoryReservationCommand rejected by inventory service during an outage)
///     - Integration events that could not be delivered after all retries are exhausted
///     </para>
///
///     <para>
///     <b>Authorization:</b> AdminElevated + AdminStrict rate limit (same as AdminOrderRecovery).
///     </para>
/// </summary>
public class AdminDlqEndpoints : IEndpointGroup
{
    public void MapEndpoints(IEndpointRouteBuilder app, ApiVersionSet versionSet)
    {
        var group = app.MapGroup("/api/admin/dlq")
            .WithApiVersionSet(versionSet)
            .HasApiVersion(1.0)
            .WithTags("Admin DLQ Management")
            .RequireAuthorization("AdminElevated")
            .RequireRateLimiting("AdminStrict");

        group.MapGet("", ListDeadLetters)
            .WithName("ListDeadLetters")
            .WithSummary("List dead-lettered messages with optional type filter and pagination")
            .Produces<DlqListResponse>(StatusCodes.Status200OK);

        group.MapPost("{id:guid}/replay", ReplayDeadLetter)
            .WithName("ReplayDeadLetter")
            .WithSummary("Mark a single dead-lettered message as replayable")
            .Produces(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("{id:guid}", DismissDeadLetter)
            .WithName("DismissDeadLetter")
            .WithSummary("Permanently dismiss (delete) a dead-lettered message without replay")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("bulk-replay", BulkReplayDeadLetters)
            .WithName("BulkReplayDeadLetters")
            .WithSummary("Mark multiple dead-lettered messages as replayable (optionally filtered by type)")
            .Produces<BulkReplayResponse>(StatusCodes.Status202Accepted);
    }

    private static async Task<IResult> ListDeadLetters(
        [FromQuery] int limit,
        [FromQuery] int offset,
        [FromQuery] string? type,
        DeadLetterEnvelopeRepository repository,
        HttpContext httpContext,
        ILogger<AdminDlqEndpoints> logger,
        CancellationToken cancellationToken)
    {
        limit = limit is > 0 and <= 200 ? limit : 50;
        offset = offset >= 0 ? offset : 0;

        logger.LogInformation(
            "Admin DLQ list requested. Limit={Limit}, Offset={Offset}, TypeFilter={TypeFilter}",
            limit, offset, type ?? "(none)");

        var items = await repository.ListAsync(limit, offset, type, cancellationToken);
        var total = await repository.CountAsync(type, cancellationToken);

        return Results.Ok(new DlqListResponse(
            items.Select(x => new DlqEnvelopeDto(
                x.Id,
                x.MessageType,
                x.Explanation,
                x.Timestamp,
                x.IsReplayable)).ToList(),
            total,
            limit,
            offset));
    }

    private static async Task<IResult> ReplayDeadLetter(
        Guid id,
        DeadLetterEnvelopeRepository repository,
        HttpContext httpContext,
        ILogger<AdminDlqEndpoints> logger,
        CancellationToken cancellationToken)
    {
        var userName = httpContext.User.Identity?.Name ?? "Unknown";

        logger.LogWarning(
            "MANUAL INTERVENTION: Marking DLQ message {Id} as replayable. User: {User}",
            id, userName);

        var found = await repository.MarkAsReplayableAsync(id, cancellationToken);

        if (!found)
        {
            logger.LogWarning("DLQ message {Id} not found", id);
            return Results.NotFound(new { MessageId = id, Error = "Dead-lettered message not found." });
        }

        return Results.Accepted(null, new
        {
            MessageId = id,
            Message = "Message marked as replayable. Wolverine durability agent will re-enqueue it shortly.",
            ProcessedBy = userName
        });
    }

    private static async Task<IResult> DismissDeadLetter(
        Guid id,
        DeadLetterEnvelopeRepository repository,
        HttpContext httpContext,
        ILogger<AdminDlqEndpoints> logger,
        CancellationToken cancellationToken)
    {
        var userName = httpContext.User.Identity?.Name ?? "Unknown";

        logger.LogWarning(
            "MANUAL INTERVENTION: Dismissing DLQ message {Id} (no replay). User: {User}",
            id, userName);

        var found = await repository.DismissAsync(id, cancellationToken);

        if (!found)
        {
            logger.LogWarning("DLQ message {Id} not found", id);
            return Results.NotFound(new { MessageId = id, Error = "Dead-lettered message not found." });
        }

        return Results.NoContent();
    }

    private static async Task<IResult> BulkReplayDeadLetters(
        BulkReplayDlqRequest request,
        DeadLetterEnvelopeRepository repository,
        HttpContext httpContext,
        ILogger<AdminDlqEndpoints> logger,
        CancellationToken cancellationToken)
    {
        var userName = httpContext.User.Identity?.Name ?? "Unknown";
        var batchLimit = request.Limit is > 0 and <= 500 ? request.Limit : 200;

        logger.LogWarning(
            "BULK MANUAL INTERVENTION: Marking up to {Limit} DLQ messages as replayable. " +
            "TypeFilter={TypeFilter}. User: {User}",
            batchLimit, request.MessageTypeFilter ?? "(all)", userName);

        var count = await repository.BulkMarkAsReplayableAsync(
            request.MessageTypeFilter,
            batchLimit,
            cancellationToken);

        return Results.Accepted(null, new BulkReplayResponse(
            count,
            request.MessageTypeFilter,
            $"Marked {count} messages as replayable. Wolverine will re-enqueue them on its next scan.",
            userName));
    }
}

// ═══════════════════════════════════════════════════════════════
// Request / Response DTOs
// ═══════════════════════════════════════════════════════════════

/// <summary>Paged list response for DLQ inspection.</summary>
public sealed record DlqListResponse(
    IReadOnlyList<DlqEnvelopeDto> Items,
    long Total,
    int Limit,
    int Offset);

/// <summary>Projection of a single dead-lettered envelope.</summary>
public sealed record DlqEnvelopeDto(
    Guid Id,
    string MessageType,
    string? Explanation,
    DateTime Timestamp,
    bool IsReplayable);

/// <summary>Request body for bulk-replay.</summary>
public sealed record BulkReplayDlqRequest(
    /// <summary>Optional message type sub-string filter (case-insensitive).</summary>
    string? MessageTypeFilter,
    /// <summary>Maximum number of messages to mark. Default: 200, max: 500.</summary>
    int Limit = 200);

/// <summary>Response for the bulk-replay operation.</summary>
public sealed record BulkReplayResponse(
    int MarkedCount,
    string? Filter,
    string Message,
    string ProcessedBy);
