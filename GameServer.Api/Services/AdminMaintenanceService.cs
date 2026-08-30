using GameServer.Api.Configuration;
using GameServer.Api.Models.Dto.Admin;
using GameServer.Api.Repositories.Interfaces;
using GameServer.Api.Services.Interfaces;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace GameServer.Api.Services;

public sealed class AdminMaintenanceService : IAdminMaintenanceService
{
    private const string FlushConfirmValue = "FLUSH_REDIS";
    private const string PlayerSaveResetConfirmValue = "RESET_PLAYER_SAVE";

    private readonly AdminToolsOptions _options;
    private readonly IRedisAdminClient _redisAdminClient;
    private readonly IPlayerAggregateRepository _playerAggregateRepository;
    private readonly ILogger<AdminMaintenanceService> _logger;

    public AdminMaintenanceService(
        IOptions<AdminToolsOptions> options,
        IRedisAdminClient redisAdminClient,
        IPlayerAggregateRepository playerAggregateRepository,
        ILogger<AdminMaintenanceService> logger)
    {
        _options = options.Value;
        _redisAdminClient = redisAdminClient;
        _playerAggregateRepository = playerAggregateRepository;
        _logger = logger;
    }

    public async Task<AdminMaintenanceOperationResponse> FlushRedisAsync(AdminRedisFlushRequest request, CancellationToken ct = default)
    {
        if (!_options.AllowRedisFlush)
        {
            return BuildFailure(
                "Redis flush is disabled for this environment.",
                AdminMaintenanceErrorCodes.FeatureDisabled);
        }

        if (request is null || !string.Equals(request.Confirm, FlushConfirmValue, StringComparison.Ordinal))
        {
            return BuildFailure(
                $"Request confirm must equal '{FlushConfirmValue}'.",
                AdminMaintenanceErrorCodes.ValidationError);
        }

        try
        {
            await _redisAdminClient.FlushDatabaseAsync(ct);
            _logger.LogWarning("Admin Redis flush executed.");
            return new AdminMaintenanceOperationResponse
            {
                Success = true
            };
        }
        catch (RedisCommandException ex)
        {
            _logger.LogError(ex, "Admin Redis flush failed.");
            return BuildFailure(
                $"Redis flush failed. Ensure the connection string enables allowAdmin. {ex.Message}",
                AdminMaintenanceErrorCodes.RedisFlushFailed);
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Admin Redis flush failed.");
            return BuildFailure(
                $"Redis flush failed. Ensure the connection string enables allowAdmin. {ex.Message}",
                AdminMaintenanceErrorCodes.RedisFlushFailed);
        }
    }

    public async Task<AdminMaintenanceOperationResponse> ResetPlayerSaveAsync(
        string playerId,
        AdminPlayerSaveResetRequest request,
        CancellationToken ct = default)
    {
        if (!_options.AllowPlayerSaveReset)
        {
            return BuildFailure(
                "Player save reset is disabled for this environment.",
                AdminMaintenanceErrorCodes.FeatureDisabled);
        }

        if (string.IsNullOrWhiteSpace(playerId))
        {
            return BuildFailure(
                "Player id is required.",
                AdminMaintenanceErrorCodes.ValidationError);
        }

        if (request is null || !string.Equals(request.Confirm, PlayerSaveResetConfirmValue, StringComparison.Ordinal))
        {
            return BuildFailure(
                $"Request confirm must equal '{PlayerSaveResetConfirmValue}'.",
                AdminMaintenanceErrorCodes.ValidationError);
        }

        try
        {
            await _playerAggregateRepository.DeleteAsync(playerId.Trim(), ct);
            _logger.LogWarning("Admin player save reset executed. PlayerId: {PlayerId}", playerId);
            return new AdminMaintenanceOperationResponse
            {
                Success = true
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Admin player save reset failed. PlayerId: {PlayerId}", playerId);
            return BuildFailure(
                "Player save reset failed.",
                AdminMaintenanceErrorCodes.PlayerSaveResetFailed);
        }
    }

    private static AdminMaintenanceOperationResponse BuildFailure(string errorMessage, string errorCode)
    {
        return new AdminMaintenanceOperationResponse
        {
            Success = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };
    }
}
