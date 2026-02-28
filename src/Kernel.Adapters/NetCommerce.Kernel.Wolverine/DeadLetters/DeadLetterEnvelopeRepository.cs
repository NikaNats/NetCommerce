#nullable enable
using Microsoft.Extensions.Options;
using Npgsql;

namespace NetCommerce.Kernel.Wolverine.DeadLetters;

/// <summary>
///     Repository for querying and managing messages in Wolverine's dead letter table.
///
///     <para>
///     <b>Replay strategy:</b> setting <c>replayable = true</c> triggers Wolverine's built-in
///     durability agent to re-enqueue the message automatically — no custom scheduler needed.
///     </para>
///
///     <para>
///     <b>Dismiss strategy:</b> deleting the row permanently removes the message. Use when the
///     message is intentionally discarded (e.g., known-bad data, superseded events).
///     </para>
/// </summary>
public sealed class DeadLetterEnvelopeRepository
{
    private readonly string _connectionString;

    public DeadLetterEnvelopeRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    ///     Lists dead-lettered messages with optional type-prefix filtering.
    ///     Results are ordered by timestamp DESC (newest failures first).
    /// </summary>
    public async Task<IReadOnlyList<DeadLetterEnvelope>> ListAsync(
        int limit = 50,
        int offset = 0,
        string? messageTypeFilter = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Const SQL strings satisfy CA2100 (no user input in query structure)
        const string filteredSql = """
            SELECT id, message_type, explanation, timestamp, replayable
            FROM wolverine.wolverine_dead_letters
            WHERE message_type ILIKE $1
            ORDER BY timestamp DESC
            LIMIT $2 OFFSET $3
            """;

        const string unfilteredSql = """
            SELECT id, message_type, explanation, timestamp, replayable
            FROM wolverine.wolverine_dead_letters
            ORDER BY timestamp DESC
            LIMIT $1 OFFSET $2
            """;

        NpgsqlCommand cmd;

        if (messageTypeFilter is not null)
        {
            cmd = new NpgsqlCommand(filteredSql, connection);
            cmd.Parameters.AddWithValue($"%{messageTypeFilter}%");
            cmd.Parameters.AddWithValue(limit);
            cmd.Parameters.AddWithValue(offset);
        }
        else
        {
            cmd = new NpgsqlCommand(unfilteredSql, connection);
            cmd.Parameters.AddWithValue(limit);
            cmd.Parameters.AddWithValue(offset);
        }

        await using (cmd)
        {
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            var results = new List<DeadLetterEnvelope>();
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(new DeadLetterEnvelope(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc),
                    reader.GetBoolean(4)));
            }

            return results;
        }
    }

    /// <summary>
    ///     Returns the total count of dead-lettered messages, optionally filtered by type.
    /// </summary>
    public async Task<long> CountAsync(
        string? messageTypeFilter = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string filteredCountSql = """
            SELECT COUNT(*) FROM wolverine.wolverine_dead_letters
            WHERE message_type ILIKE $1
            """;

        const string totalCountSql = "SELECT COUNT(*) FROM wolverine.wolverine_dead_letters";

        NpgsqlCommand cmd;

        if (messageTypeFilter is not null)
        {
            cmd = new NpgsqlCommand(filteredCountSql, connection);
            cmd.Parameters.AddWithValue($"%{messageTypeFilter}%");
        }
        else
        {
            cmd = new NpgsqlCommand(totalCountSql, connection);
        }

        await using (cmd)
        {
            return (long)(await cmd.ExecuteScalarAsync(cancellationToken) ?? 0L);
        }
    }

    /// <summary>
    ///     Marks a single dead-lettered message as replayable.
    ///     Wolverine's durability agent will pick it up and re-enqueue it on the next scan cycle.
    /// </summary>
    /// <returns><c>true</c> if the row was found and updated; <c>false</c> if not found.</returns>
    public async Task<bool> MarkAsReplayableAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE wolverine.wolverine_dead_letters
            SET replayable = true
            WHERE id = $1
            """;

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue(id);

        var rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
        return rows > 0;
    }

    /// <summary>
    ///     Permanently deletes a dead-lettered message (dismiss without replay).
    /// </summary>
    /// <returns><c>true</c> if the row was found and deleted; <c>false</c> if not found.</returns>
    public async Task<bool> DismissAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            DELETE FROM wolverine.wolverine_dead_letters
            WHERE id = $1
            """;

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue(id);

        var rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
        return rows > 0;
    }

    /// <summary>
    ///     Bulk-marks up to <paramref name="limit"/> messages as replayable.
    ///     Optionally filtered by message type (case-insensitive, partial match).
    /// </summary>
    /// <returns>Number of messages marked for replay.</returns>
    public async Task<int> BulkMarkAsReplayableAsync(
        string? messageTypeFilter = null,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Sub-select with LIMIT to avoid locking the whole table
        const string filteredBulkSql = """
            UPDATE wolverine.wolverine_dead_letters
            SET replayable = true
            WHERE id IN (
                SELECT id FROM wolverine.wolverine_dead_letters
                WHERE replayable = false AND message_type ILIKE $1
                LIMIT $2
            )
            """;

        const string unfilteredBulkSql = """
            UPDATE wolverine.wolverine_dead_letters
            SET replayable = true
            WHERE id IN (
                SELECT id FROM wolverine.wolverine_dead_letters
                WHERE replayable = false
                LIMIT $1
            )
            """;

        NpgsqlCommand cmd;

        if (messageTypeFilter is not null)
        {
            cmd = new NpgsqlCommand(filteredBulkSql, connection);
            cmd.Parameters.AddWithValue($"%{messageTypeFilter}%");
            cmd.Parameters.AddWithValue(limit);
        }
        else
        {
            cmd = new NpgsqlCommand(unfilteredBulkSql, connection);
            cmd.Parameters.AddWithValue(limit);
        }

        await using (cmd)
        {
            return await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}

/// <summary>
///     Lightweight projection of a dead-lettered message for admin inspection.
/// </summary>
public sealed record DeadLetterEnvelope(
    Guid Id,
    string MessageType,
    string? Explanation,
    DateTime Timestamp,
    bool IsReplayable);
