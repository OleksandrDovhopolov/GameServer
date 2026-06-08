using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameServer.Api.Models.Domain.Config;
using GameServer.Api.Models.Dto.Configs;
using GameServer.Api.Repositories.Interfaces;
using GameServer.Api.Services.Interfaces;

namespace GameServer.Api.Services;

public sealed class ConfigService : IConfigService
{
    private readonly IConfigRepository _repository;

    public ConfigService(IConfigRepository repository)
    {
        _repository = repository;
    }

    public async Task<ConfigManifestEntryDto[]> GetManifestAsync(string environment, CancellationToken cancellationToken)
    {
        var records = await _repository.GetManifestAsync(environment, cancellationToken);
        return records.Select(r => new ConfigManifestEntryDto
        {
            Name = r.Name,
            Version = r.Version,
            Etag = r.Etag
        }).ToArray();
    }

    public async Task<ConfigContentResult> GetConfigAsync(string name, string environment, string? ifNoneMatch, CancellationToken cancellationToken)
    {
        var record = await _repository.GetLatestAsync(name, environment, cancellationToken);
        if (record is null)
            return ConfigContentResult.NotFound();

        if (ifNoneMatch is not null && NormalizeEtag(ifNoneMatch) == record.Etag)
            return ConfigContentResult.NotModified();

        return ConfigContentResult.Success(record.Etag, record.Json);
    }

    public async Task<AdminConfigOperationResult> GetAdminConfigAsync(string name, string environment, CancellationToken cancellationToken)
    {
        var record = await _repository.GetLatestAsync(name, environment, cancellationToken);
        return record is null
            ? AdminConfigOperationResult.NotFound()
            : AdminConfigOperationResult.Success(ToAdminDto(record));
    }

    public async Task<ConfigHistoryEntryDto[]> GetHistoryAsync(string name, string environment, CancellationToken cancellationToken)
    {
        var records = await _repository.GetHistoryAsync(name, environment, cancellationToken);
        return records.Select(r => new ConfigHistoryEntryDto
        {
            Version = r.Version,
            Etag = r.Etag,
            UpdatedBy = r.UpdatedBy,
            UpdatedAt = r.UpdatedAt,
            Comment = r.Comment
        }).ToArray();
    }

    public async Task<AdminConfigOperationResult> PublishAsync(
        string name, string environment, JsonElement json,
        string ifMatch, string updatedBy, string? comment,
        CancellationToken cancellationToken)
    {
        if (!TryValidateConfigJson(json, out var validationError))
            return AdminConfigOperationResult.ValidationFailed(validationError);

        var minified = Minify(json);
        var etag = ComputeEtag(minified);
        var appendResult = await _repository.AppendAsync(
            new ConfigAppendRequest(
                name,
                environment,
                etag,
                minified,
                updatedBy,
                DateTime.UtcNow,
                comment,
                NormalizeOptionalEtag(ifMatch)),
            cancellationToken);

        return appendResult.Status switch
        {
            ConfigAppendStatus.Success => AdminConfigOperationResult.Success(ToAdminDto(appendResult.Record!)),
            ConfigAppendStatus.PreconditionFailed => AdminConfigOperationResult.PreconditionFailed(
                "A newer version exists. Pull the latest, merge, and retry."),
            _ => throw new InvalidOperationException($"Unsupported append status: {appendResult.Status}.")
        };
    }

    public async Task<AdminConfigOperationResult> RollbackAsync(
        string name, string environment, long toVersion,
        string updatedBy, CancellationToken cancellationToken)
    {
        var target = await _repository.GetByVersionAsync(name, environment, toVersion, cancellationToken);
        if (target is null)
            return AdminConfigOperationResult.NotFound(
                $"Version {toVersion} of '{name}' in environment '{environment}' not found.");

        var appendResult = await _repository.AppendAsync(
            new ConfigAppendRequest(
                name,
                environment,
                target.Etag,
                target.Json,
                updatedBy,
                DateTime.UtcNow,
                $"rollback to v{toVersion}",
                null),
            cancellationToken);

        return appendResult.Status switch
        {
            ConfigAppendStatus.Success => AdminConfigOperationResult.Success(ToAdminDto(appendResult.Record!)),
            ConfigAppendStatus.PreconditionFailed => throw new InvalidOperationException("Rollback append cannot fail precondition without an expected ETag."),
            _ => throw new InvalidOperationException($"Unsupported append status: {appendResult.Status}.")
        };
    }

    public async Task<AdminConfigOperationResult> PromoteAsync(
        string name, string fromEnvironment, string toEnvironment,
        string updatedBy, CancellationToken cancellationToken)
    {
        var source = await _repository.GetLatestAsync(name, fromEnvironment, cancellationToken);
        if (source is null)
            return AdminConfigOperationResult.NotFound(
                $"Config '{name}' not found in environment '{fromEnvironment}'.");

        var appendResult = await _repository.AppendAsync(
            new ConfigAppendRequest(
                name,
                toEnvironment,
                source.Etag,
                source.Json,
                updatedBy,
                DateTime.UtcNow,
                $"promoted from {fromEnvironment}",
                null),
            cancellationToken);

        return appendResult.Status switch
        {
            ConfigAppendStatus.Success => AdminConfigOperationResult.Success(ToAdminDto(appendResult.Record!)),
            ConfigAppendStatus.PreconditionFailed => throw new InvalidOperationException("Promote append cannot fail precondition without an expected ETag."),
            _ => throw new InvalidOperationException($"Unsupported append status: {appendResult.Status}.")
        };
    }

    public static string ComputeEtag(string minifiedJson)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(minifiedJson));
        return Convert.ToHexString(bytes)[..12].ToLowerInvariant();
    }

    private static string Minify(JsonElement element)
    {
        using var ms = new MemoryStream();
        using var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false });
        element.WriteTo(writer);
        writer.Flush();
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static bool TryValidateConfigJson(JsonElement json, out string errorMessage)
    {
        if (json.ValueKind != JsonValueKind.Array)
        {
            errorMessage = "Config payload must be a JSON array.";
            return false;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in json.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                errorMessage = "Each config item must be a JSON object.";
                return false;
            }

            if (!TryGetId(item, out var id))
            {
                errorMessage = "Each config item must contain a non-empty string 'id'.";
                return false;
            }

            if (!ids.Add(id))
            {
                errorMessage = $"Duplicate config id '{id}' is not allowed.";
                return false;
            }
        }

        errorMessage = string.Empty;
        return true;
    }

    private static bool TryGetId(JsonElement item, out string id)
    {
        foreach (var property in item.EnumerateObject())
        {
            if (!string.Equals(property.Name, "id", StringComparison.OrdinalIgnoreCase))
                continue;

            if (property.Value.ValueKind == JsonValueKind.String)
            {
                var value = property.Value.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    id = value;
                    return true;
                }
            }

            break;
        }

        id = string.Empty;
        return false;
    }

    private static string NormalizeEtag(string etag) =>
        etag.Trim('"');

    private static string? NormalizeOptionalEtag(string? etag) =>
        string.IsNullOrWhiteSpace(etag) ? null : NormalizeEtag(etag);

    private static AdminConfigDto ToAdminDto(ConfigVersionRecord r) => new()
    {
        Name = r.Name,
        Environment = r.Environment,
        Version = r.Version,
        Etag = r.Etag,
        Json = JsonSerializer.Deserialize<JsonElement>(r.Json)
    };
}
