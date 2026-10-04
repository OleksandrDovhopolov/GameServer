using GameServer.EventsWorker.Models;
using Npgsql;
using NpgsqlTypes;

namespace GameServer.EventsWorker.Services;

/// <summary>
/// Writes one row per consumed event. The primary key is (partition, offset), so re-reading the same
/// offsets after an offset reset inserts nothing new — the consumer is idempotent by construction.
/// </summary>
public sealed class PostgresGameEventRepository : IGameEventRepository
{
    private const string InsertSql = """
        INSERT INTO game_events (partition_id, event_offset, player_id, event_name, occurred_at, payload)
        VALUES (@partition_id, @event_offset, @player_id, @event_name, @occurred_at, @payload)
        ON CONFLICT (partition_id, event_offset) DO NOTHING;
        """;

    private readonly NpgsqlDataSource _dataSource;

    public PostgresGameEventRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task InsertAsync(
        int partition,
        long offset,
        GameEventEnvelope envelope,
        CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(InsertSql);
        command.Parameters.AddWithValue("partition_id", partition);
        command.Parameters.AddWithValue("event_offset", offset);
        command.Parameters.AddWithValue("player_id", envelope.PlayerId!);
        command.Parameters.AddWithValue("event_name", envelope.Event!);
        command.Parameters.AddWithValue("occurred_at", envelope.OccurredAtUtc!.Value.UtcDateTime);
        command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb)
        {
            Value = envelope.Payload.HasValue ? envelope.Payload.Value.GetRawText() : DBNull.Value
        });

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
