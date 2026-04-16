using System.Globalization;
using Dapper;
using GameServer.Api.Configuration;
using GameServer.Api.Models.Domain;
using GameServer.Api.Repositories.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace GameServer.Api.Repositories;

public sealed class PostgreSqlSaveRepository : ISaveRepository
{
    private readonly string _connectionString;
    private readonly ILogger<PostgreSqlSaveRepository> _logger;

    public PostgreSqlSaveRepository(
        IOptions<PostgresOptions> postgresOptions,
        ILogger<PostgreSqlSaveRepository> logger)
    {
        if (postgresOptions.Value is null || string.IsNullOrWhiteSpace(postgresOptions.Value.ConnectionString))
        {
            throw new InvalidOperationException("Postgres:ConnectionString is not configured.");
        }

        _connectionString = postgresOptions.Value.ConnectionString;
        _logger = logger;
    }

    public async Task SaveAsync(string playerId, string data, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("PostgreSQL SaveAsync started for {PlayerId}.", playerId);

        var lastModified = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        const string upsertSql = @"
INSERT INTO player_saves (player_id, save_data, last_modified_at)
VALUES (@PlayerId, @SaveData, @LastModifiedAt)
ON CONFLICT (player_id) DO UPDATE SET
    save_data = EXCLUDED.save_data,
    last_modified_at = EXCLUDED.last_modified_at,
    updated_at = CURRENT_TIMESTAMP;";

        using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        _logger.LogInformation("PostgreSQL connection opened for SaveAsync({PlayerId}).", playerId);

        using var tx = await conn.BeginTransactionAsync(cancellationToken);
        try
        {
            var affectedRows = await conn.ExecuteAsync(new CommandDefinition(upsertSql,
                new { PlayerId = playerId, SaveData = data, LastModifiedAt = lastModified }, transaction: tx,
                cancellationToken: cancellationToken));

            await tx.CommitAsync(cancellationToken);
            _logger.LogInformation(
                "PostgreSQL SaveAsync committed for {PlayerId}. Affected rows: {AffectedRows}, LastModified: {LastModified}.",
                playerId, affectedRows, lastModified);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "PostgreSQL SaveAsync failed for {PlayerId}. Transaction rolled back.", playerId);
            throw;
        }
    }

    public async Task<SaveRecord?> LoadAsync(string playerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("PostgreSQL LoadAsync started for {PlayerId}.", playerId);

        const string sql = @"
SELECT
    save_data AS SaveData,
    last_modified_at AS LastModifiedAt
FROM player_saves
WHERE player_id = @PlayerId;";

        using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        _logger.LogInformation("PostgreSQL connection opened for LoadAsync({PlayerId}).", playerId);

        var row = await conn.QuerySingleOrDefaultAsync<SaveRow>(new CommandDefinition(sql, new { PlayerId = playerId },
            cancellationToken: cancellationToken));

        _logger.LogInformation(
            row is null
                ? "PostgreSQL LoadAsync miss for {PlayerId}."
                : "PostgreSQL LoadAsync hit for {PlayerId}. LastModified: {LastModified}.",
            playerId, row?.LastModifiedAt);

        return row is null ? null : new SaveRecord { Data = row.SaveData, LastModified = row.LastModifiedAt };
    }

    public async Task<long?> GetLastModifiedTimestampAsync(string playerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("PostgreSQL GetLastModifiedTimestampAsync started for {PlayerId}.", playerId);

        const string sql = @"
SELECT last_modified_at
FROM player_saves
WHERE player_id = @PlayerId;";

        using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        _logger.LogInformation("PostgreSQL connection opened for GetLastModifiedTimestampAsync({PlayerId}).", playerId);

        var lastModified = await conn.QuerySingleOrDefaultAsync<long?>(new CommandDefinition(sql, new { PlayerId = playerId },
            cancellationToken: cancellationToken));

        _logger.LogInformation(
            lastModified is null
                ? "PostgreSQL GetLastModifiedTimestampAsync miss for {PlayerId}."
                : "PostgreSQL GetLastModifiedTimestampAsync hit for {PlayerId}: {LastModified}.",
            playerId, lastModified);

        return lastModified;
    }

    public async Task DeleteAsync(string playerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("PostgreSQL DeleteAsync started for {PlayerId}.", playerId);

        const string sql = @"DELETE FROM player_saves WHERE player_id = @PlayerId;";

        using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        _logger.LogInformation("PostgreSQL connection opened for DeleteAsync({PlayerId}).", playerId);

        var affectedRows = await conn.ExecuteAsync(new CommandDefinition(sql, new { PlayerId = playerId },
            cancellationToken: cancellationToken));
        _logger.LogInformation("PostgreSQL DeleteAsync completed for {PlayerId}. Affected rows: {AffectedRows}.", playerId, affectedRows);
    }

    public async Task<bool> ExistsAsync(string playerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("PostgreSQL ExistsAsync started for {PlayerId}.", playerId);

        const string sql = @"
SELECT EXISTS(
    SELECT 1
    FROM player_saves
    WHERE player_id = @PlayerId
);";

        using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        _logger.LogInformation("PostgreSQL connection opened for ExistsAsync({PlayerId}).", playerId);

        var exists = await conn.QuerySingleAsync<bool>(new CommandDefinition(sql, new { PlayerId = playerId },
            cancellationToken: cancellationToken));
        _logger.LogInformation("PostgreSQL ExistsAsync result for {PlayerId}: {Exists}.", playerId, exists);
        return exists;
    }

    private sealed class SaveRow
    {
        public required string SaveData { get; init; }
        public required long LastModifiedAt { get; init; }
    }
}
