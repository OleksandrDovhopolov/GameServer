namespace GameServer.Api.Models.Dto.Configs;

public enum ConfigOperationStatus
{
    Success,
    NotFound,
    NotModified,
    PreconditionFailed,
    ValidationFailed
}
