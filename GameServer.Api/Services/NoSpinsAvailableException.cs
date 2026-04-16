namespace GameServer.Api.Services;

public sealed class NoSpinsAvailableException : Exception
{
    public NoSpinsAvailableException(int nextRegenSeconds)
        : base("No spins available.")
    {
        NextRegenSeconds = nextRegenSeconds;
    }

    public int NextRegenSeconds { get; }
}
