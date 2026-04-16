using System.Text;
using GameServer.Api.Models.Domain;
using GameServer.Api.Repositories.Interfaces;
using GameServer.Api.Services.Interfaces;

namespace GameServer.Api.Services;

public sealed class SaveService : ISaveService
{
    private const int MaxSaveBytes = 30 * 1024;
    private readonly ISaveRepository _saveRepository;

    public SaveService(ISaveRepository saveRepository)
    {
        _saveRepository = saveRepository;
    }

    public Task SaveAsync(string playerId, string data, CancellationToken cancellationToken)
    {
        ValidatePlayerId(playerId);
        ValidateData(data);
        return _saveRepository.SaveAsync(playerId, data, cancellationToken);
    }

    public async Task<SaveRecord> LoadAsync(string playerId, CancellationToken cancellationToken)
    {
        ValidatePlayerId(playerId);
        var save = await _saveRepository.LoadAsync(playerId, cancellationToken);
        return save ?? throw new KeyNotFoundException($"Save for player '{playerId}' was not found.");
    }

    public async Task DeleteAsync(string playerId, CancellationToken cancellationToken)
    {
        ValidatePlayerId(playerId);
        var exists = await _saveRepository.ExistsAsync(playerId, cancellationToken);
        if (!exists)
        {
            throw new KeyNotFoundException($"Save for player '{playerId}' was not found.");
        }

        await _saveRepository.DeleteAsync(playerId, cancellationToken);
    }

    public async Task<long> GetLastModifiedTimestampAsync(string playerId, CancellationToken cancellationToken)
    {
        ValidatePlayerId(playerId);
        var timestamp = await _saveRepository.GetLastModifiedTimestampAsync(playerId, cancellationToken);
        return timestamp ?? throw new KeyNotFoundException($"Save for player '{playerId}' was not found.");
    }

    private static void ValidatePlayerId(string playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            throw new ArgumentException("PlayerId is required.", nameof(playerId));
        }
    }

    private static void ValidateData(string data)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            throw new ArgumentException("Data is required.", nameof(data));
        }

        var dataBytes = Encoding.UTF8.GetByteCount(data);
        if (dataBytes > MaxSaveBytes)
        {
            throw new ArgumentException($"Data exceeds the maximum allowed size of {MaxSaveBytes} bytes.", nameof(data));
        }
    }
}
