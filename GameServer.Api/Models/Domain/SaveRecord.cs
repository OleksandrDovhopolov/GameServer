namespace GameServer.Api.Models.Domain;

public sealed class SaveRecord
{
    public required string Data { get; init; }
    public required long LastModified { get; init; }
}
