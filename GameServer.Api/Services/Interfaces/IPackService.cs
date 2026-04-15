using GameServer.Api.Models.Domain;
using GameServer.Api.Models.Dto;

namespace GameServer.Api.Services.Interfaces;

public interface IPackService
{
    Task<OpenPackResponse> OpenPackAsync(OpenPackRequest request, CancellationToken ct);
}