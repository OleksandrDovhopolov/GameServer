using System.Text.Json;
using System.Text.Json.Nodes;
using GameServer.Api.Models.Dto.Inventory;
using GameServer.Api.Filters;
using GameServer.Api.Models.Dto.Admin;
using GameServer.Api.Models.Domain.Inventory;
using GameServer.Api.Repositories.Interfaces;
using GameServer.Api.Services.Interfaces;
using GameServer.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace GameServer.Api.Controllers;

[ApiController]
[Route("api/admin/player")]
[ServiceFilter(typeof(AdminBasicAuthFilter))]
public sealed class AdminController : ControllerBase
{
    private const int MaxAvailableSpins = 8;
    private const int MinAvailableSpins = 0;
    private const string FortuneWheelPropertyName = "FortuneWheel";
    private const string AvailableSpinsPropertyName = "AvailableSpins";
    private const string UpdatedAtPropertyName = "UpdatedAt";
    private const long RegenIntervalMs = 2 * 60 * 1000;

    private readonly ILogger<AdminController> _logger;
    private readonly ISaveRepository _saveRepository;
    private readonly IInventoryManagementService _inventoryManagementService;
    private readonly ISystemClock _clock;

    public AdminController(
        ILogger<AdminController> logger,
        ISaveRepository saveRepository,
        IInventoryManagementService inventoryManagementService,
        ISystemClock clock)
    {
        _logger = logger;
        _saveRepository = saveRepository;
        _inventoryManagementService = inventoryManagementService;
        _clock = clock;
    }

    [HttpPost("/api/admin/inventory/add")]
    public async Task<ActionResult<InventoryOperationResponse>> AddInventory(
        [FromBody] AddInventoryItemCommand command,
        CancellationToken cancellationToken)
    {
        using (_logger.BeginScope(new Dictionary<string, object?>
               {
                   ["PlayerId"] = command.PlayerId,
                   ["ItemId"] = command.ItemId,
                   ["Amount"] = command.Amount,
                   ["Category"] = command.Category,
                   ["Reason"] = command.Reason
               }))
        {
            var result = await _inventoryManagementService.AddAsync(command, cancellationToken);
            return MapInventoryResult(result);
        }
    }

    [HttpPost("{id}/spins")]
    public async Task<ActionResult<AdminPlayerSpinsResponse>> AdjustSpins(
        [FromRoute] string id,
        [FromQuery] int count,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest("Player id is required.");
        }

        using (_logger.BeginScope(new Dictionary<string, object> { ["PlayerId"] = id }))
        {
            var now = _clock.UtcNowUnixMilliseconds;

            var save = await _saveRepository.LoadAsync(id, cancellationToken);
            var root = ParseSaveRootOrEmpty(save?.Data);
            var wheel = GetOrCreateFortuneWheel(root);
            var currentSpins = ReadAvailableSpins(wheel);
            var updatedAt = ReadUpdatedAtOrRepair(wheel, now);

            var nextSpins = ClampSpins(currentSpins + count);
            wheel[AvailableSpinsPropertyName] = nextSpins;
            wheel[UpdatedAtPropertyName] = updatedAt;
            CanonicalizeWheelJson(root, wheel);

            var payload = root.ToJsonString();
            await _saveRepository.SaveAsync(id, payload, cancellationToken);

            var response = new AdminPlayerSpinsResponse
            {
                PlayerId = id,
                AvailableSpins = nextSpins,
                UpdatedAt = updatedAt,
                NextUpdateAt = updatedAt + RegenIntervalMs
            };

            _logger.LogInformation(
                "Admin spins updated. PlayerId: {PlayerId}, CountDelta: {CountDelta}, PreviousSpins: {PreviousSpins}, NewSpins: {NewSpins}, UpdatedAt: {UpdatedAt}",
                id,
                count,
                currentSpins,
                nextSpins,
                updatedAt);

            return Ok(response);
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeletePlayer([FromRoute] string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest("Player id is required.");
        }

        using (_logger.BeginScope(new Dictionary<string, object> { ["PlayerId"] = id }))
        {
            await _saveRepository.DeleteAsync(id, cancellationToken);
            _logger.LogInformation("Admin player data deleted.");
            return NoContent();
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetRawPlayerSave([FromRoute] string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest("Player id is required.");
        }

        using (_logger.BeginScope(new Dictionary<string, object> { ["PlayerId"] = id }))
        {
            var exists = await _saveRepository.ExistsAsync(id, cancellationToken);
            if (!exists)
            {
                _logger.LogInformation("Admin raw save requested for missing player.");
                return NotFound();
            }

            var save = await _saveRepository.LoadAsync(id, cancellationToken);
            if (save is null || string.IsNullOrWhiteSpace(save.Data))
            {
                return Ok(new JsonObject());
            }

            try
            {
                var node = JsonNode.Parse(save.Data);
                return Ok(node ?? new JsonObject());
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Admin raw save contains invalid JSON.");
                return StatusCode(StatusCodes.Status500InternalServerError, "Stored save data is not valid JSON.");
            }
        }
    }

    private static JsonObject ParseSaveRootOrEmpty(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return [];
        }

        try
        {
            if (JsonNode.Parse(payload) is JsonObject root)
            {
                return root;
            }
        }
        catch (JsonException)
        {
            // ignored - we fall back to empty object for admin repair flow.
        }

        return [];
    }

    private static JsonObject GetOrCreateFortuneWheel(JsonObject root)
    {
        if (root.TryGetPropertyValue(FortuneWheelPropertyName, out var fortuneWheelNode) &&
            fortuneWheelNode is JsonObject existing)
        {
            return existing;
        }

        var fortuneWheel = new JsonObject();
        root[FortuneWheelPropertyName] = fortuneWheel;
        return fortuneWheel;
    }

    private static int ReadAvailableSpins(JsonObject wheel)
    {
        var raw = TryGetInt(wheel, AvailableSpinsPropertyName);
        return raw is null ? MaxAvailableSpins : ClampSpins(raw.Value);
    }

    private static long ReadUpdatedAtOrRepair(JsonObject wheel, long now)
    {
        var raw = TryGetLong(wheel, UpdatedAtPropertyName);
        if (raw is null)
        {
            return now;
        }

        if (!TryNormalizeTimestamp(raw.Value, now, out var normalized))
        {
            return now;
        }

        return normalized;
    }

    private static int ClampSpins(int value)
    {
        return Math.Clamp(value, MinAvailableSpins, MaxAvailableSpins);
    }

    private static bool TryNormalizeTimestamp(long raw, long now, out long normalized)
    {
        normalized = raw;
        if (raw <= 0)
        {
            return false;
        }

        if (raw <= 9_999_999_999)
        {
            if (raw > long.MaxValue / 1000)
            {
                return false;
            }

            normalized = raw * 1000;
        }

        return normalized <= now;
    }

    private static void CanonicalizeWheelJson(JsonObject root, JsonObject wheel)
    {
        root.Remove("availableSpins");
        root.Remove("lastResetTimestamp");

        wheel.Remove("availableSpins");
        wheel.Remove("lastResetTimestamp");
        wheel.Remove("LastResetTimestamp");
        wheel.Remove("NextUpdateAt");
    }

    private static int? TryGetInt(JsonObject root, string propertyName)
    {
        if (!root.TryGetPropertyValue(propertyName, out var value) || value is null)
        {
            return null;
        }

        if (value is JsonValue jsonValue && jsonValue.TryGetValue(out int intValue))
        {
            return intValue;
        }

        if (value is JsonValue stringValue &&
            stringValue.TryGetValue(out string? raw) &&
            int.TryParse(raw, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static long? TryGetLong(JsonObject root, string propertyName)
    {
        if (!root.TryGetPropertyValue(propertyName, out var value) || value is null)
        {
            return null;
        }

        if (value is JsonValue jsonValue && jsonValue.TryGetValue(out long longValue))
        {
            return longValue;
        }

        if (value is JsonValue stringValue &&
            stringValue.TryGetValue(out string? raw) &&
            long.TryParse(raw, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private ActionResult<InventoryOperationResponse> MapInventoryResult(InventoryOperationResult result)
    {
        var response = new InventoryOperationResponse
        {
            Success = result.Success,
            ErrorCode = result.ErrorCode,
            ErrorMessage = result.ErrorMessage,
            Inventory = result.Snapshot,
            PlayerState = result.PlayerState
        };

        if (result.Success)
        {
            return Ok(response);
        }

        return response.ErrorCode switch
        {
            InventoryErrorCodes.InvalidRequest => BadRequest(response),
            InventoryErrorCodes.PlayerNotFound => NotFound(response),
            InventoryErrorCodes.InsufficientInventory => StatusCode(StatusCodes.Status409Conflict, response),
            _ => StatusCode(StatusCodes.Status500InternalServerError, response)
        };
    }
}
