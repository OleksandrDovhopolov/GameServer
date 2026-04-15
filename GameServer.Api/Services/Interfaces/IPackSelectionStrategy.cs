using GameServer.Api.Models.Domain;

namespace GameServer.Api.Services.Interfaces;

public interface IPackSelectionStrategy
{
    Task<List<string>> SelectCardsAsync(CardPack pack, List<CardDefinition> allCards, CancellationToken ct = default);
}