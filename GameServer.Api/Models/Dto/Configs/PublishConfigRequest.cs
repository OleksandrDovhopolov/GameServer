using System.Text.Json;

namespace GameServer.Api.Models.Dto.Configs;

public sealed class PublishConfigRequest
{
    public JsonElement Json { get; init; }
    public string? Comment { get; init; }
}
