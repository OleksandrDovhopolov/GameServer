namespace GameServer.Api.Models.Dto.Admin;

public sealed class AdminRedisFlushRequest
{
    public string Confirm { get; init; } = string.Empty;
}

public sealed class AdminMaintenanceOperationResponse
{
    public bool Success { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}
