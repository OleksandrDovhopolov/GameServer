using GameServer.Api.Models.Dto.Save;
using GameServer.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace GameServer.Api.Controllers;

[ApiController]
[Route("api/v1/save/global")]
public sealed class SaveController : ControllerBase
{
    private readonly ILogger<SaveController> _logger;
    private readonly ISaveService _saveService;

    public SaveController(ILogger<SaveController> logger, ISaveService saveService)
    {
        _logger = logger;
        _saveService = saveService;
    }

    [HttpPost]
    public async Task<IActionResult> Save([FromBody] SaveRequest request, CancellationToken cancellationToken)
    {
        using (_logger.BeginScope(new Dictionary<string, object> { ["PlayerId"] = request.PlayerId }))
        {
            try
            {
                _logger.LogInformation("Save request received. Payload size: {PayloadSize}", request.Data.Length);
                await _saveService.SaveAsync(request.PlayerId, request.Data, cancellationToken);
                return Ok();
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Save request validation failed.");
                return BadRequest(ex.Message);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Save request canceled.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Save request failed.");
                return StatusCode(StatusCodes.Status500InternalServerError, "Internal error while saving data.");
            }
        }
    }

    [HttpGet]
    public async Task<ActionResult<SaveLoadResponse>> Load([FromQuery] string playerId, CancellationToken cancellationToken)
    {
        using (_logger.BeginScope(new Dictionary<string, object> { ["PlayerId"] = playerId }))
        {
            try
            {
                _logger.LogInformation("Load request received.");
                var result = await _saveService.LoadAsync(playerId, cancellationToken);
                return Ok(new SaveLoadResponse
                {
                    Data = result.Data,
                    LastModified = result.LastModified
                });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Load request validation failed.");
                return BadRequest(ex.Message);
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning(ex, "Save not found.");
                return NotFound(ex.Message);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Load request canceled.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Load request failed.");
                return StatusCode(StatusCodes.Status500InternalServerError, "Internal error while loading data.");
            }
        }
    }

    [HttpDelete]
    public async Task<IActionResult> Delete([FromQuery] string playerId, CancellationToken cancellationToken)
    {
        using (_logger.BeginScope(new Dictionary<string, object> { ["PlayerId"] = playerId }))
        {
            try
            {
                _logger.LogInformation("Delete request received.");
                await _saveService.DeleteAsync(playerId, cancellationToken);
                return Ok();
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Delete request validation failed.");
                return BadRequest(ex.Message);
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning(ex, "Save not found.");
                return NotFound(ex.Message);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Delete request canceled.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Delete request failed.");
                return StatusCode(StatusCodes.Status500InternalServerError, "Internal error while deleting data.");
            }
        }
    }

    [HttpGet("meta")]
    public async Task<ActionResult<SaveMetaResponse>> GetMeta([FromQuery] string playerId, CancellationToken cancellationToken)
    {
        using (_logger.BeginScope(new Dictionary<string, object> { ["PlayerId"] = playerId }))
        {
            try
            {
                _logger.LogInformation("Metadata request received.");
                var timestamp = await _saveService.GetLastModifiedTimestampAsync(playerId, cancellationToken);
                return Ok(new SaveMetaResponse { LastModified = timestamp });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Metadata request validation failed.");
                return BadRequest(ex.Message);
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning(ex, "Save metadata not found.");
                return NotFound(ex.Message);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Metadata request canceled.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Metadata request failed.");
                return StatusCode(StatusCodes.Status500InternalServerError, "Internal error while reading metadata.");
            }
        }
    }
}
