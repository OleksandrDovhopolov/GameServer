namespace GameServer.Api.Models.Domain.Config;

public sealed record ConfigAppendRequest(
    string Name,
    string Environment,
    string Etag,
    string Json,
    string UpdatedBy,
    DateTime UpdatedAt,
    string? Comment,
    string? ExpectedCurrentEtag
);
