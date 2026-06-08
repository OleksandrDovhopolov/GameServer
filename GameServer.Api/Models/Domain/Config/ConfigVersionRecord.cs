namespace GameServer.Api.Models.Domain.Config;

public sealed record ConfigVersionRecord(
    string Name,
    string Environment,
    long Version,
    string Etag,
    string Json,
    string UpdatedBy,
    DateTime UpdatedAt,
    string? Comment
);
