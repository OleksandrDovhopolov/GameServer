namespace GameServer.Api.Models.Dto.Configs;

public sealed class ConfigManifestEntryDto
{
    public string Name { get; init; } = string.Empty;
    public long Version { get; init; }
    public string Etag { get; init; } = string.Empty;
}
