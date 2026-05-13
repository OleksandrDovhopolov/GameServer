namespace GameServer.Api.Models.Dto.LiveOps;

public sealed class PublicLiveOpsScheduleResponseDto
{
    public string ServerTimeUtc { get; init; } = string.Empty;
    public IReadOnlyList<PublicLiveOpsScheduleItemDto> Items { get; init; } = [];
}
