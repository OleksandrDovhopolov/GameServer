using System.Text.Json;
using GameServer.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameServer.Api.Tests;

public sealed class LiveOpsScheduleServiceTests
{
    [Fact]
    public async Task GetScheduleAsync_ReadsItemsInFileOrder()
    {
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
                "Id": "second",
                "EventType": "BattlePass",
                "StartTimeUtc": "2026-04-20T22:25:43.4664352+00:00",
                "EndTimeUtc": "2026-05-01T22:34:03.4664352+00:00",
                "Priority": 1,
                "StreamId": "battle_pass",
                "CustomParams": {}
              }
            ]
            """);
        var service = new FileLiveOpsScheduleService(data.Environment, NullLogger<FileLiveOpsScheduleService>.Instance);

        var items = await service.GetScheduleAsync(CancellationToken.None);

        Assert.Collection(
            items,
            first =>
            {
                Assert.Equal("first", first.Id);
                Assert.Equal(10, first.Priority);
                Assert.Single(first.CustomParams);
            },
            second =>
            {
                Assert.Equal("second", second.Id);
                Assert.Equal(1, second.Priority);
                Assert.Empty(second.CustomParams);
            });
    }

    [Fact]
    public async Task GetScheduleAsync_WhenFileMissing_ThrowsFileNotFoundException()
    {
        using var data = TestDataEnvironment.CreateWithoutSchedule();
        var service = new FileLiveOpsScheduleService(data.Environment, NullLogger<FileLiveOpsScheduleService>.Instance);

        await Assert.ThrowsAsync<FileNotFoundException>(() => service.GetScheduleAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetScheduleAsync_WhenJsonInvalid_ThrowsJsonException()
    {
        using var data = TestDataEnvironment.Create("{");
        var service = new FileLiveOpsScheduleService(data.Environment, NullLogger<FileLiveOpsScheduleService>.Instance);

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
        var service = new FileLiveOpsScheduleService(data.Environment, NullLogger<FileLiveOpsScheduleService>.Instance);

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
}
