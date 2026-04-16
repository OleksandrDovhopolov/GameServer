namespace GameServer.Api.Models.Dto.Save;

public sealed class SaveLoadResponse
{
    public required string Data { get; init; }
    public required long LastModified { get; init; }
}
