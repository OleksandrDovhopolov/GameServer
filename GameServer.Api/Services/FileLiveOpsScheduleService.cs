using System.Text.Json;
using System.Text.Json.Serialization;
using GameServer.Api.Models.Domain.BattlePass;
using GameServer.Api.Models.Dto.LiveOps;
using GameServer.Api.Services.Interfaces;

namespace GameServer.Api.Services;

public sealed class FileLiveOpsScheduleService : ILiveOpsScheduleService
{
    private const string ScheduleFileName = "liveops_schedule.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<FileLiveOpsScheduleService> _logger;
    private readonly ILiveOpsScheduleItemProvider _liveOpsScheduleItemProvider;
    private readonly ISystemClock _clock;

    public FileLiveOpsScheduleService(
        IWebHostEnvironment environment,
        ILogger<FileLiveOpsScheduleService> logger,
        ILiveOpsScheduleItemProvider liveOpsScheduleItemProvider,
        ISystemClock clock)
    {
        _environment = environment;
        _logger = logger;
        _liveOpsScheduleItemProvider = liveOpsScheduleItemProvider;
        _clock = clock;
    }

    public async Task<PublicLiveOpsScheduleResponseDto> GetScheduleAsync(CancellationToken ct = default)
    {
        var result = await LoadFileItemsAsync(ct);
        var generatedItems = await _liveOpsScheduleItemProvider.GetItemsAsync(ct);
        foreach (var generatedItem in generatedItems)
        {
            result.Add(new PublicLiveOpsScheduleItemDto
            {
                Id = generatedItem.Id,
                EventType = generatedItem.EventType,
                StartTimeUtc = UnixTimeHelper.ToUtcIsoString(generatedItem.StartTimeUtcMs),
                EndTimeUtc = UnixTimeHelper.ToUtcIsoString(generatedItem.EndTimeUtcMs),
                Priority = generatedItem.Priority,
                StreamId = generatedItem.StreamId,
                CustomParams = new Dictionary<string, string>(generatedItem.CustomParams, StringComparer.Ordinal)
            });
        }

        _logger.LogInformation(
            "Loaded {FileItemCount} file liveops items and {GeneratedItemCount} generated liveops items.",
            result.Count - generatedItems.Count,
            generatedItems.Count);

        return new PublicLiveOpsScheduleResponseDto
        {
            ServerTimeUtc = UnixTimeHelper.ToUtcIsoString(_clock.UtcNowUnixMilliseconds),
            Items = result
        };
    }

    private async Task<List<PublicLiveOpsScheduleItemDto>> LoadFileItemsAsync(CancellationToken ct)
    {
        var filePath = Path.Combine(_environment.ContentRootPath, "Data", ScheduleFileName);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("LiveOps schedule configuration file was not found.", filePath);
        }

        await using var stream = File.OpenRead(filePath);
        var items = await JsonSerializer.DeserializeAsync<List<LiveOpsScheduleFileItem>>(stream, JsonOptions, ct);
        if (items is null)
        {
            throw new InvalidOperationException("LiveOps schedule configuration is empty.");
        }

        var result = new List<PublicLiveOpsScheduleItemDto>(items.Count);
        for (var index = 0; index < items.Count; index++)
        {
            var mapped = MapAndValidate(items[index], index);
            if (string.Equals(mapped.EventType, "BattlePass", StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "Ignoring file-defined BattlePass liveops item '{ScheduleItemId}'. BattlePass schedule is generated from season repository.",
                    mapped.Id);
                continue;
            }

            result.Add(mapped);
        }

        return result;
    }

    private static PublicLiveOpsScheduleItemDto MapAndValidate(LiveOpsScheduleFileItem item, int index)
    {
        if (string.IsNullOrWhiteSpace(item.Id) ||
            string.IsNullOrWhiteSpace(item.EventType) ||
            string.IsNullOrWhiteSpace(item.StartTimeUtc) ||
            string.IsNullOrWhiteSpace(item.EndTimeUtc) ||
            !item.Priority.HasValue ||
            string.IsNullOrWhiteSpace(item.StreamId) ||
            item.CustomParams is null)
        {
            throw new InvalidOperationException($"LiveOps schedule item at index {index} is missing required fields.");
        }

        return new PublicLiveOpsScheduleItemDto
        {
            Id = item.Id,
            EventType = item.EventType,
            StartTimeUtc = item.StartTimeUtc,
            EndTimeUtc = item.EndTimeUtc,
            Priority = item.Priority.Value,
            StreamId = item.StreamId,
            CustomParams = new Dictionary<string, string>(item.CustomParams, StringComparer.Ordinal)
        };
    }

    private sealed class LiveOpsScheduleFileItem
    {
        [JsonPropertyName("Id")]
        public string? Id { get; init; }

        [JsonPropertyName("EventType")]
        public string? EventType { get; init; }

        [JsonPropertyName("StartTimeUtc")]
        public string? StartTimeUtc { get; init; }

        [JsonPropertyName("EndTimeUtc")]
        public string? EndTimeUtc { get; init; }

        [JsonPropertyName("Priority")]
        public int? Priority { get; init; }

        [JsonPropertyName("StreamId")]
        public string? StreamId { get; init; }

        [JsonPropertyName("CustomParams")]
        public Dictionary<string, string>? CustomParams { get; init; }
    }
}
