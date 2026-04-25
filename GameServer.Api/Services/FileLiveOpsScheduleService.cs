using System.Text.Json;
using System.Text.Json.Serialization;
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

    public FileLiveOpsScheduleService(IWebHostEnvironment environment, ILogger<FileLiveOpsScheduleService> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PublicLiveOpsScheduleItemDto>> GetScheduleAsync(CancellationToken ct = default)
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
            result.Add(MapAndValidate(items[index], index));
        }

        _logger.LogInformation("Loaded {ItemCount} liveops schedule items from file.", result.Count);
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
