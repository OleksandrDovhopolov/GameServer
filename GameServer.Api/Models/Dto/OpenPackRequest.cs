namespace GameServer.Api.Models.Dto;

public class OpenPackRequest
{
    public string PlayerId { get; init; }
    public string PackId { get; init; }
    public string EventId { get; init; }
    public string OpenPackRequestId { get; init; }
}
