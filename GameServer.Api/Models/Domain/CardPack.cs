namespace GameServer.Api.Models.Domain;

public class CardPack
{
    private readonly CardPackConfig _config;
    public int PurchaseCount { get; private set; }

    public string PackId => _config.packId;
    public int CardCount => _config.cardCount;
    public string PackName => _config.packName;
        
    public CardPack(CardPackConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public void OnPurchased()
    {
        PurchaseCount++;
    }

    public override string ToString()
    {
        return $"Pack: {_config.packName} ({_config.cardCount} cards)";
    }
}