namespace GameServer.Api.Models.Dto.Admin;

public sealed class SaveStorageBackfillResponse
{
    public bool Success { get; init; }
    public int Scanned { get; init; }
    public int Written { get; init; }
    public int Skipped { get; init; }
    public int Failed { get; init; }
    public List<string> Errors { get; init; } = [];
}

public sealed class SaveStorageStatusResponse
{
    public string Provider { get; init; } = string.Empty;
    public int RedisPlayers { get; init; }
    public int PostgresRows { get; init; }
}
