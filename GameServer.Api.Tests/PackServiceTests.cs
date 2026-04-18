using GameServer.Api.Models.Domain;
using GameServer.Api.Models.Dto;
using GameServer.Api.Services;
using GameServer.Api.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace GameServer.Api.Tests;

public sealed class PackServiceTests
{
    [Fact]
    public async Task OpenPackAsync_WhenWinterEventId_LoadsWinterCardDatabase()
    {
        using var data = TestDataEnvironment.Create("winter-card", "spring-card");
        var strategy = new CapturingStrategy();
        var service = new PackService(strategy, data.Environment);
        var request = CreateRequest("Winter_Collection");

        var response = await service.OpenPackAsync(request, CancellationToken.None);

        Assert.Contains("winter-card", strategy.LastSeenCardIds);
        Assert.DoesNotContain("spring-card", strategy.LastSeenCardIds);
        Assert.Single(response.OpenedCardIds);
        Assert.Equal("winter-card", response.OpenedCardIds[0]);
    }

    [Fact]
    public async Task OpenPackAsync_WhenSpringEventId_LoadsSpringCardDatabase()
    {
        using var data = TestDataEnvironment.Create("winter-card", "spring-card");
        var strategy = new CapturingStrategy();
        var service = new PackService(strategy, data.Environment);
        var request = CreateRequest("Spring_Collection");

        var response = await service.OpenPackAsync(request, CancellationToken.None);

        Assert.Contains("spring-card", strategy.LastSeenCardIds);
        Assert.DoesNotContain("winter-card", strategy.LastSeenCardIds);
        Assert.Single(response.OpenedCardIds);
        Assert.Equal("spring-card", response.OpenedCardIds[0]);
    }

    [Fact]
    public async Task OpenPackAsync_WhenEventIdUnknown_ThrowsArgumentException()
    {
        using var data = TestDataEnvironment.Create("winter-card", "spring-card");
        var strategy = new CapturingStrategy();
        var service = new PackService(strategy, data.Environment);
        var request = CreateRequest("Summer_Collection");

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => service.OpenPackAsync(request, CancellationToken.None));

        Assert.Equal("eventId", exception.ParamName);
        Assert.Contains("Unsupported EventId", exception.Message);
        Assert.Contains("Winter_Collection", exception.Message);
        Assert.Contains("Spring_Collection", exception.Message);
    }

    [Fact]
    public async Task OpenPackAsync_WhenEventIdMissing_ThrowsArgumentException()
    {
        using var data = TestDataEnvironment.Create("winter-card", "spring-card");
        var strategy = new CapturingStrategy();
        var service = new PackService(strategy, data.Environment);
        var request = CreateRequest(string.Empty);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => service.OpenPackAsync(request, CancellationToken.None));

        Assert.Equal("eventId", exception.ParamName);
        Assert.Contains("EventId is required.", exception.Message);
    }

    private static OpenPackRequest CreateRequest(string eventId)
    {
        return new OpenPackRequest
        {
            PlayerId = "p1",
            PackId = "starter_pack",
            EventId = eventId,
            OpenPackRequestId = "open-1"
        };
    }

    private sealed class CapturingStrategy : IPackSelectionStrategy
    {
        public List<string> LastSeenCardIds { get; private set; } = new();

        public Task<List<string>> SelectCardsAsync(CardPack pack, List<CardDefinition> allCards, CancellationToken ct = default)
        {
            LastSeenCardIds = allCards.Select(card => card.Id).ToList();
            return Task.FromResult(LastSeenCardIds.Take(pack.CardCount).ToList());
        }
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

        public static TestDataEnvironment Create(string winterCardId, string springCardId)
        {
            var rootPath = Path.Combine(Path.GetTempPath(), "GameServer-PackServiceTests-" + Guid.NewGuid().ToString("N"));
            var dataPath = Path.Combine(rootPath, "Data");
            Directory.CreateDirectory(dataPath);

            File.WriteAllText(
                Path.Combine(dataPath, "CardPackDatabse.json"),
                """
                {
                  "packs": [
                    {
                      "packId": "starter_pack",
                      "packName": "Starter Pack",
                      "cardCount": 1
                    }
                  ]
                }
                """);

            File.WriteAllText(
                Path.Combine(dataPath, "Winter_Collection_CardDatabase.json"),
                CreateCardDatabaseJson(winterCardId));

            File.WriteAllText(
                Path.Combine(dataPath, "Spring_Collection_CardDatabase.json"),
                CreateCardDatabaseJson(springCardId));

            return new TestDataEnvironment(rootPath);
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, true);
            }
        }

        private static string CreateCardDatabaseJson(string cardId)
        {
            return $$"""
                     {
                       "cards": [
                         {
                           "id": "{{cardId}}",
                           "cardName": "Card {{cardId}}",
                           "groupType": "test-group",
                           "stars": 1,
                           "premiumCard": false,
                           "icon": "test-icon"
                         }
                       ]
                     }
                     """;
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
