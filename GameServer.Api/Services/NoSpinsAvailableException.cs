namespace GameServer.Api.Services;

public sealed class NoSpinsAvailableException : Exception
{
    public NoSpinsAvailableException(long updatedAt, long nextUpdateAt)
        : base("No spins available.")
    {
        UpdatedAt = updatedAt;
        NextUpdateAt = nextUpdateAt;
    }

    public long UpdatedAt { get; }
    public long NextUpdateAt { get; }
}
