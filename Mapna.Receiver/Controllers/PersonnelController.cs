using Mapna.Contracts;
using Mapna.LogData;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Mapna.Receiver.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
[EnableRateLimiting("PerClientLimit")]
public class PersonnelController : ControllerBase
{
    public const string CorrelationHeader = "X-Correlation-Id";

    private readonly PersonnelUpsertService _upsertService;
    private readonly ILogger<PersonnelController> _logger;

    public PersonnelController(PersonnelUpsertService upsertService, ILogger<PersonnelController> logger)
    {
        _upsertService = upsertService;
        _logger = logger;
    }

    /// <summary>
    /// Status codes are a contract with the sender's retry logic:
    ///   200 = applied or already identical (safe to record as sent)
    ///   409 = permanent business conflict (don't retry)
    ///   422 = permanent validation failure (don't retry)
    ///   500 = unexpected; the sender retries a bounded number of times
    /// HttpContext.RequestAborted is deliberately NOT passed down: once we start a write we finish it,
    /// so a client disconnect never leaves the outcome half-way.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Post([FromBody] PersonnelRecord record, [FromHeader(Name = CorrelationHeader)] Guid? correlationId)
    {
        try
        {
            var result = await _upsertService.ProcessAsync(record, correlationId);
            var body = new { perId = record.PerId, status = result.Status.ToString(), changedFields = result.ChangedFields, conflictingPerId = result.ConflictingPerId };
            return result.Status switch
            {
                ReceiveStatus.ValidationFailed => UnprocessableEntity(body),
                ReceiveStatus.RejectedNationalCodeConflict => Conflict(body),
                _ => Ok(body)
            };
        }
        catch (DbUpdateException ex) when (PersonnelUpsertService.IsUniqueViolation(ex))
        {
            // Only reachable if a writer bypassed the locking path. The DB index is the last line of defence.
            _logger.LogWarning(ex, "Unique constraint rejected PerId {PerId} (correlation {CorrelationId})", record.PerId, correlationId);
            return Conflict(new { perId = record.PerId, status = nameof(ReceiveStatus.RejectedNationalCodeConflict) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while processing PerId {PerId} (correlation {CorrelationId})", record.PerId, correlationId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { perId = record.PerId, status = "Error" });
        }
    }
}
