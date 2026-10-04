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
