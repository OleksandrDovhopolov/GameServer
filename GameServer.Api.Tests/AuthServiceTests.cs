using GameServer.Api.Configuration;
using GameServer.Api.Models.Domain.Auth;
using GameServer.Api.Repositories.Interfaces;
using GameServer.Api.Services;
using GameServer.Api.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GameServer.Api.Tests;

public sealed class AuthServiceTests
{
    [Fact]
    public async Task Anonymous_CreatesPlayerAndIdentity()
    {
        var accounts = new InMemoryPlayerAccountRepository();
        var refreshTokens = new InMemoryRefreshTokenRepository();
        var service = CreateService(accounts, refreshTokens);

        var result = await service.AuthenticateAnonymousAsync(null, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Tokens);
        Assert.True(accounts.HasPlayer(result.Tokens!.PlayerId));
        Assert.True(accounts.HasIdentity(AuthProviders.Anonymous, result.Tokens.PlayerId) ||
                    accounts.IdentitiesForPlayer(result.Tokens.PlayerId).Any(x => x.Provider == AuthProviders.Anonymous));
    }

    [Fact]
    public async Task Anonymous_WithLegacyPlayerId_ClaimsSamePlayerId()
    {
        var accounts = new InMemoryPlayerAccountRepository();
        var service = CreateService(accounts, new InMemoryRefreshTokenRepository());
        var legacyPlayerId = Guid.NewGuid().ToString("D");

        var result = await service.AuthenticateAnonymousAsync(legacyPlayerId, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(legacyPlayerId, result.Tokens!.PlayerId);
        Assert.True(accounts.HasIdentity(AuthProviders.Legacy, legacyPlayerId));
    }

    [Fact]
    public async Task Anonymous_WhenLegacyAlreadyClaimed_ReturnsConflictCode()
    {
        var service = CreateService(new InMemoryPlayerAccountRepository(), new InMemoryRefreshTokenRepository());
        var legacyPlayerId = Guid.NewGuid().ToString("D");

        var first = await service.AuthenticateAnonymousAsync(legacyPlayerId, CancellationToken.None);
        var second = await service.AuthenticateAnonymousAsync(legacyPlayerId, CancellationToken.None);

        Assert.True(first.Success);
        Assert.False(second.Success);
        Assert.Equal(AuthErrorCodes.LegacyAlreadyClaimed, second.ErrorCode);
    }

    [Fact]
    public async Task Anonymous_WhenLegacyClaimDisabled_ReturnsDisabledCode()
    {
        var service = CreateService(
            new InMemoryPlayerAccountRepository(),
            new InMemoryRefreshTokenRepository(),
            new AuthOptions
            {
                JwtSigningKey = TestSigningKey,
                AllowLegacyClaim = false
            });

        var result = await service.AuthenticateAnonymousAsync(Guid.NewGuid().ToString("D"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(AuthErrorCodes.LegacyClaimDisabled, result.ErrorCode);
    }

    [Fact]
    public async Task Refresh_RotatesTokenAndRejectsUsedToken()
    {
        var service = CreateService(new InMemoryPlayerAccountRepository(), new InMemoryRefreshTokenRepository());
        var login = await service.AuthenticateAnonymousAsync(null, CancellationToken.None);

        var refresh = await service.RefreshAsync(login.Tokens!.RefreshToken, CancellationToken.None);
        var reuse = await service.RefreshAsync(login.Tokens.RefreshToken, CancellationToken.None);

        Assert.True(refresh.Success);
        Assert.NotEqual(login.Tokens.RefreshToken, refresh.Tokens!.RefreshToken);
        Assert.False(reuse.Success);
        Assert.Equal(AuthErrorCodes.InvalidRefreshToken, reuse.ErrorCode);
    }

    [Fact]
    public async Task Login_WhenProviderMissing_ReturnsProviderNotSupported()
    {
        var service = CreateService(new InMemoryPlayerAccountRepository(), new InMemoryRefreshTokenRepository());

        var result = await service.LoginAsync("google", "token", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(AuthErrorCodes.ProviderNotSupported, result.ErrorCode);
    }

    private const string TestSigningKey = "0123456789abcdef0123456789abcdef";

    private static AuthService CreateService(
        InMemoryPlayerAccountRepository accounts,
        InMemoryRefreshTokenRepository refreshTokens,
        AuthOptions? options = null)
    {
        return new AuthService(
            accounts,
            refreshTokens,
            new StubJwtTokenService(),
            [],
            Options.Create(options ?? new AuthOptions { JwtSigningKey = TestSigningKey }),
            NullLogger<AuthService>.Instance);
    }

    private sealed class StubJwtTokenService : IJwtTokenService
    {
        public string IssueAccessToken(string playerId, DateTime utcNow, out DateTime expiresAt)
        {
            expiresAt = utcNow.AddMinutes(60);
            return $"access:{playerId}:{Guid.NewGuid():N}";
        }
    }

    private sealed class InMemoryPlayerAccountRepository : IPlayerAccountRepository
    {
        private readonly Dictionary<string, PlayerAccount> _players = new(StringComparer.Ordinal);
        private readonly Dictionary<(string Provider, string Subject), PlayerIdentity> _identities = new();

        public bool HasPlayer(string playerId) => _players.ContainsKey(playerId);

        public bool HasIdentity(string provider, string subject) => _identities.ContainsKey((provider, subject));

        public IEnumerable<PlayerIdentity> IdentitiesForPlayer(string playerId) =>
            _identities.Values.Where(x => string.Equals(x.PlayerId, playerId, StringComparison.Ordinal));

        public Task<PlayerAccount?> GetByIdentityAsync(string provider, string subject, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!_identities.TryGetValue((provider, subject), out var identity))
            {
                return Task.FromResult<PlayerAccount?>(null);
            }

            return Task.FromResult(_players.TryGetValue(identity.PlayerId, out var player) ? player : null);
        }

        public Task<PlayerAccount?> GetByPlayerIdAsync(string playerId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(_players.TryGetValue(playerId, out var player) ? player : null);
        }

        public Task<bool> CreateWithIdentityAsync(string playerId, string provider, string subject, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (_identities.ContainsKey((provider, subject)))
            {
                return Task.FromResult(false);
            }

            _players.TryAdd(playerId, new PlayerAccount(playerId, DateTime.UtcNow, null));
            _identities[(provider, subject)] = new PlayerIdentity(provider, subject, playerId, DateTime.UtcNow);
            return Task.FromResult(true);
        }

        public Task<bool> LinkIdentityAsync(string playerId, string provider, string subject, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!_players.ContainsKey(playerId) || _identities.ContainsKey((provider, subject)))
            {
                return Task.FromResult(false);
            }

            _identities[(provider, subject)] = new PlayerIdentity(provider, subject, playerId, DateTime.UtcNow);
            return Task.FromResult(true);
        }

        public Task TouchLastSeenAsync(string playerId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (_players.TryGetValue(playerId, out var player))
            {
                _players[playerId] = player with { LastSeenAt = DateTime.UtcNow };
            }

            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryRefreshTokenRepository : IRefreshTokenRepository
    {
        private readonly Dictionary<string, TokenRecord> _tokens = new(StringComparer.Ordinal);

        public Task IssueAsync(string tokenHash, string playerId, DateTime expiresAt, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _tokens[tokenHash] = new TokenRecord(playerId, expiresAt, false);
            return Task.CompletedTask;
        }

        public Task<string?> RotateAsync(string oldTokenHash, string newTokenHash, DateTime newExpiresAt, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!_tokens.TryGetValue(oldTokenHash, out var oldToken) ||
                oldToken.Revoked ||
                oldToken.ExpiresAt <= DateTime.UtcNow)
            {
                return Task.FromResult<string?>(null);
            }

            _tokens[oldTokenHash] = oldToken with { Revoked = true };
            _tokens[newTokenHash] = new TokenRecord(oldToken.PlayerId, newExpiresAt, false);
            return Task.FromResult<string?>(oldToken.PlayerId);
        }

        public Task RevokeAllForPlayerAsync(string playerId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var (hash, token) in _tokens.ToArray())
            {
                if (string.Equals(token.PlayerId, playerId, StringComparison.Ordinal))
                {
                    _tokens[hash] = token with { Revoked = true };
                }
            }

            return Task.CompletedTask;
        }

        private sealed record TokenRecord(string PlayerId, DateTime ExpiresAt, bool Revoked);
    }
}
