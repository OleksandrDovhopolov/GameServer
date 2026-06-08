namespace GameServer.Api.Models.Dto.Configs;

public sealed class ConfigHistoryEntryDto
{
    public long Version { get; init; }
    public string Etag { get; init; } = string.Empty;
    public string UpdatedBy { get; init; } = string.Empty;
    public DateTime UpdatedAt { get; init; }
    public string? Comment { get; init; }
}
