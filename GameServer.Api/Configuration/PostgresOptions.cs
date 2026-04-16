namespace GameServer.Api.Configuration;

public sealed class PostgresOptions
{
    public const string SectionName = "Postgres";

    public string ConnectionString { get; init; } =
        "Host=localhost;Port=5432;Database=game_server;Username=postgres;Password=postgres;Include Error Detail=true";
}

