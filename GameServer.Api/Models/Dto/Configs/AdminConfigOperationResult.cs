namespace GameServer.Api.Models.Dto.Configs;

public sealed class AdminConfigOperationResult
{
    private AdminConfigOperationResult(
        ConfigOperationStatus status,
        AdminConfigDto? config = null,
        string? errorMessage = null)
    {
        Status = status;
        Config = config;
        ErrorMessage = errorMessage;
    }

    public ConfigOperationStatus Status { get; }

    public AdminConfigDto? Config { get; }

    public string? ErrorMessage { get; }

    public static AdminConfigOperationResult Success(AdminConfigDto config) =>
        new(ConfigOperationStatus.Success, config);

    public static AdminConfigOperationResult NotFound(string? errorMessage = null) =>
        new(ConfigOperationStatus.NotFound, errorMessage: errorMessage);

    public static AdminConfigOperationResult PreconditionFailed(string? errorMessage = null) =>
        new(ConfigOperationStatus.PreconditionFailed, errorMessage: errorMessage);

    public static AdminConfigOperationResult ValidationFailed(string errorMessage) =>
        new(ConfigOperationStatus.ValidationFailed, errorMessage: errorMessage);
}
