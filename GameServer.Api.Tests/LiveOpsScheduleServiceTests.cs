using System.Text.Json;
using GameServer.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GameServer.Api.Tests;

public sealed class LiveOpsScheduleServiceTests
{
    [Fact]
    public async Task GetScheduleAsync_ReadsFileItemsInFileOrder_AndAppendsGeneratedBattlePassItems()
    {
        const long now = 1_777_777_777_000;
        using var data = TestDataEnvironment.Create(
            """
            [
              {
                "Id": "first",
                "EventType": "CardCollection",
                "StartTimeUtc": "2026-04-16T08:39:03.4664352+00:00",
                "EndTimeUtc": "2026-04-27T22:25:43.4664352+00:00",
                "Priority": 10,
                "StreamId": "cards",
                "CustomParams": {
                  "eventId": "first"
                }
              },
              {
                "Id": "file_bp",
                "EventType": "BattlePass",
                "StartTimeUtc": "2026-04-20T22:25:43.4664352+00:00",
                "EndTimeUtc": "2026-05-01T22:34:03.4664352+00:00",
                "Priority": 1,
                "StreamId": "battle_pass",
                "CustomParams": {}
              },
              {
                "Id": "second",
                "EventType": "CardCollection",
                "StartTimeUtc": "2026-05-01T00:00:00+00:00",
                "EndTimeUtc": "2026-05-10T00:00:00+00:00",
                "Priority": 7,
                "StreamId": "cards",
                "CustomParams": {
                  "eventId": "second"
                }
              }
            ]
            """);
        var seasonRepository = new InMemoryBattlePassSeasonRepository();
        var season = BattlePassTestFactory.CreateSeason("Test_Battle_Pass", now - 10_000, now + 10_000);
        await seasonRepository.SaveAsync(season, CancellationToken.None);

        var logger = new ListLogger<FileLiveOpsScheduleService>();
        var service = new FileLiveOpsScheduleService(
            data.Environment,
            logger,
            new BattlePassLiveOpsProvider(seasonRepository, new FixedClock(now)),
            new FixedClock(now));

        var response = await service.GetScheduleAsync(CancellationToken.None);

        Assert.Equal(UnixTimeHelper.ToUtcIsoString(now), response.ServerTimeUtc);
        Assert.Collection(
            response.Items,
            first =>
            {
                Assert.Equal("first", first.Id);
                Assert.Equal("CardCollection", first.EventType);
                Assert.Equal(10, first.Priority);
            },
            second =>
            {
                Assert.Equal("second", second.Id);
                Assert.Equal("CardCollection", second.EventType);
                Assert.Equal(7, second.Priority);
            },
            battlePass =>
            {
                Assert.Equal("Test_Battle_Pass", battlePass.Id);
                Assert.Equal("BattlePass", battlePass.EventType);
                Assert.Equal("battle_pass", battlePass.StreamId);
                Assert.Equal(1, battlePass.Priority);
                Assert.Equal("Test_Battle_Pass", battlePass.CustomParams["seasonId"]);
                Assert.Equal("Test_Battle_Pass", battlePass.CustomParams["title"]);
                Assert.Equal("v1", battlePass.CustomParams["configVersion"]);
            });

        Assert.Contains(
            logger.Entries,
            entry => entry.LogLevel == LogLevel.Warning &&
                     entry.Message.Contains("Ignoring file-defined BattlePass liveops item 'file_bp'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetScheduleAsync_WhenFileMissing_ThrowsFileNotFoundException()
    {
        using var data = TestDataEnvironment.CreateWithoutSchedule();
        var service = new FileLiveOpsScheduleService(
            data.Environment,
            new ListLogger<FileLiveOpsScheduleService>(),
            new StaticLiveOpsScheduleItemProvider(),
            new FixedClock(1));

        await Assert.ThrowsAsync<FileNotFoundException>(() => service.GetScheduleAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetScheduleAsync_WhenJsonInvalid_ThrowsJsonException()
    {
        using var data = TestDataEnvironment.Create("{");
        var service = new FileLiveOpsScheduleService(
            data.Environment,
            new ListLogger<FileLiveOpsScheduleService>(),
            new StaticLiveOpsScheduleItemProvider(),
            new FixedClock(1));

        await Assert.ThrowsAsync<JsonException>(() => service.GetScheduleAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetScheduleAsync_WhenRequiredFieldMissing_ThrowsInvalidOperationException()
    {
        using var data = TestDataEnvironment.Create(
            """
            [
              {
                "Id": "first",
                "EventType": "CardCollection",
                "StartTimeUtc": "2026-04-16T08:39:03.4664352+00:00",
                "EndTimeUtc": "2026-04-27T22:25:43.4664352+00:00",
                "StreamId": "cards",
                "CustomParams": {}
              }
            ]
            """);
        var service = new FileLiveOpsScheduleService(
            data.Environment,
            new ListLogger<FileLiveOpsScheduleService>(),
            new StaticLiveOpsScheduleItemProvider(),
            new FixedClock(1));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetScheduleAsync(CancellationToken.None));
    }

    private sealed class TestDataEnvironment : IDisposable
    {
        private TestDataEnvironment(string rootPath)
        {
            RootPath = rootPath;
            Environment = new TestWebHostEnvironment(rootPath);
        }

        public string RootPath { get; }
        public IWebHostEnvironment Environment { get; }

        public static TestDataEnvironment Create(string scheduleJson)
        {
            var rootPath = Path.Combine(Path.GetTempPath(), "GameServer-LiveOpsScheduleServiceTests-" + Guid.NewGuid().ToString("N"));
            var dataPath = Path.Combine(rootPath, "Data");
            Directory.CreateDirectory(dataPath);
            File.WriteAllText(Path.Combine(dataPath, "liveops_schedule.json"), scheduleJson);
            return new TestDataEnvironment(rootPath);
        }

        public static TestDataEnvironment CreateWithoutSchedule()
        {
            var rootPath = Path.Combine(Path.GetTempPath(), "GameServer-LiveOpsScheduleServiceTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(rootPath, "Data"));
            return new TestDataEnvironment(rootPath);
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, true);
            }
        }
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public TestWebHostEnvironment(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
            ContentRootFileProvider = new NullFileProvider();
            WebRootPath = contentRootPath;
            WebRootFileProvider = new NullFileProvider();
            ApplicationName = "GameServer.Api.Tests";
            EnvironmentName = "Development";
        }

        public string ApplicationName { get; set; }
        public IFileProvider WebRootFileProvider { get; set; }
        public string WebRootPath { get; set; }
        public string EnvironmentName { get; set; }
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; }
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullDisposable.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }

        public sealed record LogEntry(LogLevel LogLevel, string Message);

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();
            public void Dispose()
            {
            }
        }
    }
}
