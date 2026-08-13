using Dapper;
using GameServer.Api.Models.Domain.Auth;
using GameServer.Api.Repositories.Interfaces;
using Npgsql;

namespace GameServer.Api.Repositories;

public sealed class PostgresPlayerAccountRepository : IPlayerAccountRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresPlayerAccountRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<PlayerAccount?> GetByIdentityAsync(string provider, string subject, CancellationToken ct)
    {
        const string sql = """
            SELECT p.player_id, p.created_at, p.last_seen_at
            FROM player_identities i
            INNER JOIN players p ON p.player_id = i.player_id
            WHERE i.provider = @provider AND i.subject = @subject
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(ct);
        var row = await connection.QueryFirstOrDefaultAsync<PlayerRow>(new CommandDefinition(
            sql,
            new { provider, subject },
            cancellationToken: ct));
        return row is null ? null : ToAccount(row);
    }

    public async Task<PlayerAccount?> GetByPlayerIdAsync(string playerId, CancellationToken ct)
    {
        const string sql = """
            SELECT player_id, created_at, last_seen_at
            FROM players
            WHERE player_id = @playerId
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(ct);
        var row = await connection.QueryFirstOrDefaultAsync<PlayerRow>(new CommandDefinition(
            sql,
            new { playerId },
            cancellationToken: ct));
        return row is null ? null : ToAccount(row);
    }

    public async Task<bool> CreateWithIdentityAsync(string playerId, string provider, string subject, CancellationToken ct)
    {
        const string playerSql = """
            INSERT INTO players (player_id)
            VALUES (@playerId)
            ON CONFLICT (player_id) DO NOTHING
            """;

        const string identitySql = """
            INSERT INTO player_identities (provider, subject, player_id)
            VALUES (@provider, @subject, @playerId)
            ON CONFLICT (provider, subject) DO NOTHING
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition(
            playerSql,
            new { playerId },
            transaction,
            cancellationToken: ct));

        var affected = await connection.ExecuteAsync(new CommandDefinition(
            identitySql,
            new { provider, subject, playerId },
            transaction,
            cancellationToken: ct));

        await transaction.CommitAsync(ct);
        return affected == 1;
    }

    public async Task<bool> LinkIdentityAsync(string playerId, string provider, string subject, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO player_identities (provider, subject, player_id)
            SELECT @provider, @subject, @playerId
            WHERE EXISTS (SELECT 1 FROM players WHERE player_id = @playerId)
            ON CONFLICT (provider, subject) DO NOTHING
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(ct);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { provider, subject, playerId },
            cancellationToken: ct));
        return affected == 1;
    }

    public async Task TouchLastSeenAsync(string playerId, CancellationToken ct)
    {
        const string sql = """
            UPDATE players
            SET last_seen_at = now()
            WHERE player_id = @playerId
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { playerId },
            cancellationToken: ct));
    }

    private static PlayerAccount ToAccount(PlayerRow row) =>
        new(row.player_id, row.created_at, row.last_seen_at);

    private sealed record PlayerRow(string player_id, DateTime created_at, DateTime? last_seen_at);
}

public sealed class PostgresRefreshTokenRepository : IRefreshTokenRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresRefreshTokenRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task IssueAsync(string tokenHash, string playerId, DateTime expiresAt, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO refresh_tokens (token_hash, player_id, expires_at)
            VALUES (@tokenHash, @playerId, @expiresAt)
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { tokenHash, playerId, expiresAt },
            cancellationToken: ct));
    }

    public async Task<string?> RotateAsync(string oldTokenHash, string newTokenHash, DateTime newExpiresAt, CancellationToken ct)
    {
        const string consumeSql = """
            UPDATE refresh_tokens
            SET revoked_at = now()
            WHERE token_hash = @oldTokenHash
              AND revoked_at IS NULL
              AND expires_at > now()
            RETURNING player_id
            """;

        const string issueSql = """
            INSERT INTO refresh_tokens (token_hash, player_id, expires_at)
            VALUES (@newTokenHash, @playerId, @newExpiresAt)
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        var playerId = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            consumeSql,
            new { oldTokenHash },
            transaction,
            cancellationToken: ct));

        if (string.IsNullOrWhiteSpace(playerId))
        {
            await transaction.RollbackAsync(ct);
            return null;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            issueSql,
            new { newTokenHash, playerId, newExpiresAt },
            transaction,
            cancellationToken: ct));

        await transaction.CommitAsync(ct);
        return playerId;
    }

    public async Task RevokeAllForPlayerAsync(string playerId, CancellationToken ct)
    {
        const string sql = """
            UPDATE refresh_tokens
            SET revoked_at = now()
            WHERE player_id = @playerId AND revoked_at IS NULL
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { playerId },
            cancellationToken: ct));
    }
}
