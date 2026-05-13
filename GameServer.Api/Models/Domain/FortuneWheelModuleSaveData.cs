namespace GameServer.Api.Models.Domain;

public sealed class FortuneWheelModuleSaveData
{
    public int AvailableSpins { get; set; }
    public long UpdatedAt { get; set; }
    public long NextUpdateAt { get; set; }
    public bool AdSpinAvailable { get; set; }
}
