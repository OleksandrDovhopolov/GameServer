using GameServer.Api.Configuration;
using GameServer.Api.Models.Dto.Admin;
using GameServer.Api.Services.Interfaces;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace GameServer.Api.Services;

public sealed class AdminMaintenanceService : IAdminMaintenanceService
{
    private const string FlushConfirmValue = "FLUSH_REDIS";

    private readonly AdminToolsOptions _options;
    private readonly IRedisAdminClient _redisAdminClient;
    private readonly ILogger<AdminMaintenanceService> _logger;

    public AdminMaintenanceService(
        IOptions<AdminToolsOptions> options,
        IRedisAdminClient redisAdminClient,
        ILogger<AdminMaintenanceService> logger)
    {
        _options = options.Value;
        _redisAdminClient = redisAdminClient;
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
