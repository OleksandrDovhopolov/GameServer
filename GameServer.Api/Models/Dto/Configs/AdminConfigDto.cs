using System.Text.Json;

namespace GameServer.Api.Models.Dto.Configs;

public sealed class AdminConfigDto
{
    public string Name { get; init; } = string.Empty;
    public string Environment { get; init; } = string.Empty;
    public long Version { get; init; }
    public string Etag { get; init; } = string.Empty;
    public JsonElement Json { get; init; }
}
