using Dapper;
using GameServer.Api.Models.Domain.Config;
using GameServer.Api.Repositories.Interfaces;
using Npgsql;

namespace GameServer.Api.Repositories;

public sealed class PostgresConfigRepository : IConfigRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresConfigRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<ConfigVersionRecord[]> GetManifestAsync(string environment, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT DISTINCT ON (name)
                name, environment, version, etag, json::text AS json, updated_by, updated_at, comment
            FROM configs
            WHERE environment = @environment
            ORDER BY name, version DESC
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<ConfigRow>(sql, new { environment });
        return rows.Select(ToRecord).ToArray();
    }

    public async Task<ConfigVersionRecord?> GetLatestAsync(string name, string environment, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT name, environment, version, etag, json::text AS json, updated_by, updated_at, comment
            FROM configs
            WHERE name = @name AND environment = @environment
            ORDER BY version DESC
            LIMIT 1
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QueryFirstOrDefaultAsync<ConfigRow>(sql, new { name, environment });
        return row is null ? null : ToRecord(row);
    }

    public async Task<ConfigVersionRecord?> GetByVersionAsync(string name, string environment, long version, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT name, environment, version, etag, json::text AS json, updated_by, updated_at, comment
            FROM configs
            WHERE name = @name AND environment = @environment AND version = @version
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QueryFirstOrDefaultAsync<ConfigRow>(sql, new { name, environment, version });
        return row is null ? null : ToRecord(row);
    }

    public async Task<ConfigVersionRecord[]> GetHistoryAsync(string name, string environment, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT name, environment, version, etag, json::text AS json, updated_by, updated_at, comment
            FROM configs
            WHERE name = @name AND environment = @environment
            ORDER BY version DESC
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<ConfigRow>(sql, new { name, environment });
        return rows.Select(ToRecord).ToArray();
    }

    public async Task<ConfigAppendResult> AppendAsync(ConfigAppendRequest request, CancellationToken cancellationToken)
    {
        const string advisoryLockSql = """
            SELECT pg_advisory_xact_lock(hashtext(@lockKey))
            """;

        const string latestSql = """
            SELECT name, environment, version, etag, json::text AS json, updated_by, updated_at, comment
            FROM configs
            WHERE name = @name AND environment = @environment
            ORDER BY version DESC
            LIMIT 1
            FOR UPDATE
            """;

        const string sql = """
            INSERT INTO configs (name, environment, version, etag, json, updated_by, updated_at, comment)
            VALUES (@name, @environment, @version, @etag, @json::jsonb, @updatedBy, @updatedAt, @comment)
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            advisoryLockSql,
            new { lockKey = $"{request.Name}:{request.Environment}" },
            transaction,
            cancellationToken: cancellationToken));

        var current = await connection.QueryFirstOrDefaultAsync<ConfigRow>(new CommandDefinition(
            latestSql,
            new { name = request.Name, environment = request.Environment },
            transaction,
            cancellationToken: cancellationToken));

        if (request.ExpectedCurrentEtag is not null &&
            current is not null &&
            !string.Equals(current.etag, request.ExpectedCurrentEtag, StringComparison.Ordinal))
        {
            await transaction.RollbackAsync(cancellationToken);
            return ConfigAppendResult.PreconditionFailed();
        }

        var record = new ConfigVersionRecord(
            request.Name,
            request.Environment,
            (current?.version ?? 0) + 1,
            request.Etag,
            request.Json,
            request.UpdatedBy,
            request.UpdatedAt,
            request.Comment);

        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            name = record.Name,
            environment = record.Environment,
            version = record.Version,
            etag = record.Etag,
            json = record.Json,
            updatedBy = record.UpdatedBy,
            updatedAt = record.UpdatedAt,
            comment = record.Comment
        }, transaction, cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return ConfigAppendResult.Success(record);
    }

    private static ConfigVersionRecord ToRecord(ConfigRow row) =>
        new(row.name, row.environment, row.version, row.etag, row.json, row.updated_by, row.updated_at, row.comment);

    private sealed record ConfigRow(
        string name,
        string environment,
        long version,
        string etag,
        string json,
        string updated_by,
        DateTime updated_at,
        string? comment
    );
}
