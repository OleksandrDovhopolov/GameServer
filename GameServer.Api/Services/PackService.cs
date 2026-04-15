using GameServer.Api.Models.Dto;
using GameServer.Api.Models.Domain;
using GameServer.Api.Services.Interfaces;
using System.Text.Json;

namespace GameServer.Api.Services;

public class PackService : IPackService
{
    private readonly IPackSelectionStrategy _strategy;
    private readonly IWebHostEnvironment _environment;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        IncludeFields = true
    };

    public PackService(IPackSelectionStrategy strategy, IWebHostEnvironment environment)
    {
        _strategy = strategy;
        _environment = environment;
    }

    public async Task<OpenPackResponse> OpenPackAsync(OpenPackRequest request, CancellationToken ct)
    {
        var packs = await LoadPacksAsync(ct);
        var pack = packs.FirstOrDefault(p => p.PackId == request.PackId)
            ?? throw new InvalidOperationException($"Pack with id '{request.PackId}' was not found.");
        var cards = await LoadCardsAsync(ct);

        var result = await _strategy.SelectCardsAsync(pack, cards, ct);

        return new OpenPackResponse
        {
            OpenedCardIds = result
        };
    }

    private async Task<List<CardPack>> LoadPacksAsync(CancellationToken ct)
    {
        var filePath = Path.Combine(_environment.ContentRootPath, "Data", "CardPackDatabse.json");
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Card pack database file was not found.", filePath);
        }

        await using var stream = File.OpenRead(filePath);
        var db = await JsonSerializer.DeserializeAsync<CardPackDatabase>(stream, JsonOptions, ct);
        var configs = db?.Packs ?? new List<CardPackConfig>();
        return configs.Select(config => new CardPack(config)).ToList();
    }

    private async Task<List<CardDefinition>> LoadCardsAsync(CancellationToken ct)
    {
        var filePath = Path.Combine(_environment.ContentRootPath, "Data", "CardDatabase.json");
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Card database file was not found.", filePath);
        }

        await using var stream = File.OpenRead(filePath);
        var db = await JsonSerializer.DeserializeAsync<CardDatabase>(stream, JsonOptions, ct);
        return db?.Cards ?? new List<CardDefinition>();
    }

    private sealed class CardDatabase
    {
        public List<CardDefinition> Cards { get; set; } = new();
    }

    private sealed class CardPackDatabase
    {
        public List<CardPackConfig> Packs { get; set; } = new();
    }
}