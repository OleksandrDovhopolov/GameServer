using System.Text.Json.Nodes;

namespace GameServer.Api.Models.Dto.Save;

public sealed class SaveLoadResponse
{
    public required JsonNode Data { get; init; }
    public required long LastModified { get; init; }
}
