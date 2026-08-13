using System.IdentityModel.Tokens.Jwt;
using System.Text;
using GameServer.Api.Configuration;
using GameServer.Api.Services;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace GameServer.Api.Tests;

public sealed class JwtTokenServiceTests
{
    [Fact]
    public void IssueAccessToken_CanBeValidatedAndContainsSubject()
    {
        var service = CreateService(SigningKey);

        var token = service.IssueAccessToken("player_1", DateTime.UtcNow, out _);
        var principal = Validate(token, SigningKey, "gameserver");

        Assert.Equal("player_1", principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value);
        Assert.Equal("player_1", principal.FindFirst("player_id")?.Value);
    }

    [Fact]
    public void IssueAccessToken_WithWrongKey_FailsValidation()
    {
        var service = CreateService(SigningKey);
        var token = service.IssueAccessToken("player_1", DateTime.UtcNow, out _);

        Assert.ThrowsAny<SecurityTokenException>(() => Validate(token, WrongSigningKey, "gameserver"));
    }

    [Fact]
    public void IssueAccessToken_WhenExpired_FailsValidation()
    {
        var service = new JwtTokenService(Options.Create(new AuthOptions
        {
            JwtSigningKey = SigningKey,
            Issuer = "gameserver",
            AccessTokenMinutes = 1
        }));
        var token = service.IssueAccessToken("player_1", DateTime.UtcNow.AddMinutes(-2), out _);

        Assert.ThrowsAny<SecurityTokenException>(() => Validate(token, SigningKey, "gameserver"));
    }

    private const string SigningKey = "0123456789abcdef0123456789abcdef";
    private const string WrongSigningKey = "abcdef0123456789abcdef0123456789";

    private static JwtTokenService CreateService(string key) =>
        new(Options.Create(new AuthOptions
        {
            JwtSigningKey = key,
            Issuer = "gameserver",
            AccessTokenMinutes = 60
        }));

    private static System.Security.Claims.ClaimsPrincipal Validate(string token, string key, string issuer)
    {
        var handler = new JwtSecurityTokenHandler
        {
            MapInboundClaims = false
        };
        return handler.ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        }, out _);
    }
}
