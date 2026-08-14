using System.Security.Cryptography;
using System.Text;
using GameServer.Api.Configuration;
using GameServer.Api.Models.Domain.Auth;
using GameServer.Api.Repositories.Interfaces;
using GameServer.Api.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace GameServer.Api.Services;

public sealed class AuthService : IAuthService
{
    private readonly IPlayerAccountRepository _accounts;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IEnumerable<IExternalIdentityVerifier> _externalVerifiers;
    private readonly AuthOptions _options;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IPlayerAccountRepository accounts,
        IRefreshTokenRepository refreshTokens,
        IJwtTokenService jwtTokenService,
        IEnumerable<IExternalIdentityVerifier> externalVerifiers,
        IOptions<AuthOptions> options,
        ILogger<AuthService> logger)
    {
        _accounts = accounts;
        _refreshTokens = refreshTokens;
        _jwtTokenService = jwtTokenService;
        _externalVerifiers = externalVerifiers;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AuthResult> AuthenticateAnonymousAsync(string? legacyPlayerId, CancellationToken ct)
    {
        if (!IsConfigured())
        {
            return AuthResult.Fail(AuthErrorCodes.AuthNotConfigured, "Auth signing key is not configured.");
        }

        var normalizedLegacyPlayerId = NormalizeOptional(legacyPlayerId);
        if (normalizedLegacyPlayerId is not null)
        {
            return await ClaimLegacyAsync(normalizedLegacyPlayerId, ct);
        }

        var playerId = Guid.NewGuid().ToString("N");
        var subject = Guid.NewGuid().ToString("N");
        var created = await _accounts.CreateWithIdentityAsync(playerId, AuthProviders.Anonymous, subject, ct);
        if (!created)
        {
            return AuthResult.Fail(AuthErrorCodes.IdentityAlreadyLinked, "Anonymous identity is already linked.");
        }

        return await IssueTokensAsync(playerId, ct);
    }

    public async Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        if (!IsConfigured())
        {
            return AuthResult.Fail(AuthErrorCodes.AuthNotConfigured, "Auth signing key is not configured.");
        }

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return AuthResult.Fail(AuthErrorCodes.InvalidRefreshToken, "Refresh token is invalid.");
        }

        var newRefreshToken = GenerateRefreshToken();
        var playerId = await _refreshTokens.RotateAsync(
            HashRefreshToken(refreshToken),
            HashRefreshToken(newRefreshToken),
            DateTime.UtcNow.AddDays(_options.RefreshTokenDays),
            ct);

        if (string.IsNullOrWhiteSpace(playerId))
        {
            _logger.LogWarning("Refresh token reuse or invalid refresh token detected.");
            return AuthResult.Fail(AuthErrorCodes.InvalidRefreshToken, "Refresh token is invalid.");
        }

        await _accounts.TouchLastSeenAsync(playerId, ct);
        return IssueAccessAndRefresh(playerId, newRefreshToken);
    }

    public Task<AuthResult> LoginAsync(string provider, string token, CancellationToken ct)
    {
        if (!IsConfigured())
        {
            return Task.FromResult(AuthResult.Fail(AuthErrorCodes.AuthNotConfigured, "Auth signing key is not configured."));
        }

        var verifier = FindVerifier(provider);
        return verifier is null
            ? Task.FromResult(AuthResult.Fail(AuthErrorCodes.ProviderNotSupported, "Provider is not supported."))
            : LoginWithVerifierAsync(verifier, token, ct);
    }

    public Task<AuthResult> LinkAsync(string playerId, string provider, string token, CancellationToken ct)
    {
        if (!IsConfigured())
        {
            return Task.FromResult(AuthResult.Fail(AuthErrorCodes.AuthNotConfigured, "Auth signing key is not configured."));
        }

        var verifier = FindVerifier(provider);
        return verifier is null
            ? Task.FromResult(AuthResult.Fail(AuthErrorCodes.ProviderNotSupported, "Provider is not supported."))
            : LinkWithVerifierAsync(playerId, verifier, token, ct);
    }

    public static string HashRefreshToken(string refreshToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private async Task<AuthResult> ClaimLegacyAsync(string legacyPlayerId, CancellationToken ct)
    {
        if (!_options.AllowLegacyClaim)
        {
            return AuthResult.Fail(AuthErrorCodes.LegacyClaimDisabled, "Legacy player claim is disabled.");
        }

        if (!Guid.TryParse(legacyPlayerId, out var parsed))
        {
            return AuthResult.Fail(AuthErrorCodes.ValidationError, "Legacy player id must be a GUID.");
        }

        var playerId = parsed.ToString("N");
        var existing = await _accounts.GetByIdentityAsync(AuthProviders.Legacy, playerId, ct);
        if (existing is not null)
        {
            return AuthResult.Fail(AuthErrorCodes.LegacyAlreadyClaimed, "Legacy player id is already claimed.");
        }

        var created = await _accounts.CreateWithIdentityAsync(playerId, AuthProviders.Legacy, playerId, ct);
        if (!created)
        {
            return AuthResult.Fail(AuthErrorCodes.LegacyAlreadyClaimed, "Legacy player id is already claimed.");
        }

        return await IssueTokensAsync(playerId, ct);
    }

    private async Task<AuthResult> IssueTokensAsync(string playerId, CancellationToken ct)
    {
        var refreshToken = GenerateRefreshToken();
        await _refreshTokens.IssueAsync(
            HashRefreshToken(refreshToken),
            playerId,
            DateTime.UtcNow.AddDays(_options.RefreshTokenDays),
            ct);
        return IssueAccessAndRefresh(playerId, refreshToken);
    }

    private AuthResult IssueAccessAndRefresh(string playerId, string refreshToken)
    {
        var accessToken = _jwtTokenService.IssueAccessToken(playerId, DateTime.UtcNow, out var expiresAt);
        var expiresInSeconds = Math.Max(0, (int)Math.Round((expiresAt - DateTime.UtcNow).TotalSeconds));
        return AuthResult.Ok(new IssuedTokens(playerId, accessToken, refreshToken, expiresInSeconds));
    }

    private async Task<AuthResult> LoginWithVerifierAsync(IExternalIdentityVerifier verifier, string token, CancellationToken ct)
    {
        var externalIdentity = await verifier.VerifyAsync(token, ct);
        if (externalIdentity is null)
        {
            return AuthResult.Fail(AuthErrorCodes.ValidationError, "External identity token is invalid.");
        }

        var account = await _accounts.GetByIdentityAsync(externalIdentity.Provider, externalIdentity.Subject, ct);
        if (account is null)
        {
            return AuthResult.Fail(AuthErrorCodes.IdentityAlreadyLinked, "Identity is not linked to a player.");
        }

        await _accounts.TouchLastSeenAsync(account.PlayerId, ct);
        return await IssueTokensAsync(account.PlayerId, ct);
    }

    private async Task<AuthResult> LinkWithVerifierAsync(string playerId, IExternalIdentityVerifier verifier, string token, CancellationToken ct)
    {
        var externalIdentity = await verifier.VerifyAsync(token, ct);
        if (externalIdentity is null)
        {
            return AuthResult.Fail(AuthErrorCodes.ValidationError, "External identity token is invalid.");
        }

        var linked = await _accounts.LinkIdentityAsync(playerId, externalIdentity.Provider, externalIdentity.Subject, ct);
        return linked
            ? await IssueTokensAsync(playerId, ct)
            : AuthResult.Fail(AuthErrorCodes.IdentityAlreadyLinked, "Identity is already linked.");
    }

    private IExternalIdentityVerifier? FindVerifier(string provider)
    {
        return _externalVerifiers.FirstOrDefault(x =>
            string.Equals(x.Provider, provider, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsConfigured() => !string.IsNullOrWhiteSpace(_options.JwtSigningKey);

    private static string GenerateRefreshToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
