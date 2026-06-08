namespace GameServer.Api.Models.Dto.Configs;

public sealed class ConfigContentResult
{
    private ConfigContentResult(ConfigOperationStatus status, string? etag = null, string? json = null)
    {
        Status = status;
        Etag = etag;
        Json = json;
    }

    public ConfigOperationStatus Status { get; }

    public string? Etag { get; }

    public string? Json { get; }

    public static ConfigContentResult Success(string etag, string json) =>
        new(ConfigOperationStatus.Success, etag, json);

    public static ConfigContentResult NotFound() =>
        new(ConfigOperationStatus.NotFound);

    public static ConfigContentResult NotModified() =>
        new(ConfigOperationStatus.NotModified);
}
