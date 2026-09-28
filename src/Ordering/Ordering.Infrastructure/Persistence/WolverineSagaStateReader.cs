#nullable enable
using System.Data;
using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NetCommerce.Domain.Shared;
using NetCommerce.Ordering.Application.Sagas;

namespace NetCommerce.Ordering.Infrastructure.Persistence;

/// <summary>
///     Snapshot of one live <see cref="OrderFulfillmentSaga"/> row.
/// </summary>
public sealed record SagaStateRow(
    Guid OrderId,
    string OrderNumber,
    OrderFulfillmentState State,
    string? PaymentTransactionId,
    string? FailureReason,
    DateTime StartedAt,
    Money TotalAmount);

/// <summary>
///     Reads live saga state directly from Wolverine's saga store table
///     (<c>wolverine.orderfulfillmentsaga_saga</c>, auto-provisioned by Wolverine's
///     own PostgreSQL message store).
///
///     WHY NOT <c>db.Set&lt;OrderFulfillmentSaga&gt;()</c>? Wolverine persists sagas
///     through its message-store storage, NOT through any module DbContext model —
///     the saga type is not mapped, so <c>Set&lt;&gt;</c> throws
///     <see cref="InvalidOperationException"/> ("Cannot create a DbSet ... not
///     included in the model"). That previously crash-looped SagaMonitorService
///     and StuckSagaAlertService and broke every admin recovery path that
///     touched saga state. Raw SQL against Wolverine's table (the same approach
///     as the DLQ/test dead-letter readers) is the supported read path here;
///     messages arriving for removed sagas land in the saga's NotFound handlers.
/// </summary>
public static class WolverineSagaStateReader
{
    /// <summary>
    ///     Lists live sagas, optionally filtered by state.
    ///     Only live sagas are stored (completed ones are deleted), so the
    ///     result set is small; filtering happens in memory after tolerant
    ///     JSON parsing (unparseable rows are skipped, never fatal).
    /// </summary>
    public static async Task<IReadOnlyList<SagaStateRow>> QuerySagasAsync(
        DbContext db,
        OrderFulfillmentState? state = null,
        int? maxRows = null,
        CancellationToken cancellationToken = default)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State is not ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
        // Intentionally not closed: the owning DbContext scope disposes it.
        // Reads join any ambient transaction, which is fine for observability.

        // Constant SQL (CA2100-clean): the live-saga table stays small because
        // completed sagas are deleted, so LIMIT/OFFSET and state filtering
        // happen in memory below after tolerant parsing.
        const string sql = "SELECT id, body FROM wolverine.orderfulfillmentsaga_saga";

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        var rows = new List<SagaStateRow>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = TryParseRow(reader.GetGuid(0), reader.GetString(1));
            if (row is null)
                continue;

            if (state.HasValue && row.State != state.Value)
                continue;

            rows.Add(row);

            if (maxRows.HasValue && rows.Count >= maxRows.Value)
                break;
        }

        return rows;
    }

    /// <summary>
    ///     Deletes a saga row (force-complete). Late messages for the removed
    ///     saga are handled by the saga's NotFound handlers by design.
    ///     Returns the number of rows deleted (0 when already gone).
    /// </summary>
    public static async Task<int> DeleteSagaAsync(
        DbContext db,
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State is not ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM wolverine.orderfulfillmentsaga_saga WHERE id = @id";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "id";
        parameter.Value = orderId;
        command.Parameters.Add(parameter);

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static SagaStateRow? TryParseRow(Guid id, string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            var state = ParseState(GetProperty(root, "State"));
            if (state is null)
                return null;

            var totalAmount = ParseMoney(GetProperty(root, "TotalAmount"));

            return new SagaStateRow(
                id,
                GetString(GetProperty(root, "OrderNumber")) ?? id.ToString("N")[..8],
                state.Value,
                GetString(GetProperty(root, "PaymentTransactionId")),
                GetString(GetProperty(root, "FailureReason")),
                GetDateTime(GetProperty(root, "StartedAt")) ?? DateTime.UtcNow,
                totalAmount);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonElement? GetProperty(JsonElement root, string name)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        }

        return null;
    }

    private static string? GetString(JsonElement? element)
    {
        return element is { ValueKind: JsonValueKind.String } e
            ? e.GetString()
            : null;
    }

    private static OrderFulfillmentState? ParseState(JsonElement? element)
    {
        if (element is not { } e)
            return null;

        if (e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var numeric))
            return Enum.IsDefined(typeof(OrderFulfillmentState), numeric)
                ? (OrderFulfillmentState)numeric
                : null;

        if (e.ValueKind == JsonValueKind.String
            && Enum.TryParse<OrderFulfillmentState>(e.GetString(), ignoreCase: true, out var named))
            return named;

        return null;
    }

    private static DateTime? GetDateTime(JsonElement? element)
    {
        if (element is { ValueKind: JsonValueKind.String } e
            && DateTime.TryParse(e.GetString(), out var parsed))
            return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);

        return null;
    }

    private static Money ParseMoney(JsonElement? element)
    {
        if (element is { ValueKind: JsonValueKind.Object } obj)
        {
            var amount = GetDecimal(GetProperty(obj, "Amount")) ?? 0m;
            var currency = GetString(GetProperty(obj, "Currency")) ?? "GEL";
            return Money.Create(amount, currency);
        }

        return Money.Zero();
    }

    private static decimal? GetDecimal(JsonElement? element)
    {
        if (element is not { } e)
            return null;

        return e.ValueKind switch
        {
            JsonValueKind.Number when e.TryGetDecimal(out var d) => d,
            JsonValueKind.String when decimal.TryParse(e.GetString(), out var parsed) => parsed,
            _ => null
        };
    }
}
