namespace GameServer.Api.Models.Dto.Events;

public sealed record GameEventResponse(bool Published, int Partition, long Offset);
