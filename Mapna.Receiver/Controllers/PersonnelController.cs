using Mapna.Contracts;
using Mapna.LogData;
using Microsoft.AspNetCore.Mvc;

namespace Mapna.Receiver.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PersonnelController : ControllerBase
{
    private readonly PesonnelUpsertService _pesonnelUpsertService;
    private readonly ILogger<PersonnelController> _logger;

    public PersonnelController(PesonnelUpsertService pesonnelUpsertService,ILogger<PersonnelController> logger)
    {
        _logger = logger;
        _pesonnelUpsertService = pesonnelUpsertService;
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Post([FromBody] PersonnelRecord record) 
    {
        if (record == null)
            return BadRequest("Record is null");
        try
        {
            var status = await _pesonnelUpsertService.ProcessAsync(record);
            return status switch
            {
                ReceiveStatus.ValidationFailed => UnprocessableEntity(new { perId = record.PerId, status = status.ToString() }),
                _ => Ok(new { perId = record.PerId, status = status.ToString() })
            };
        }
        catch(Exception ex)
        {
            _logger.LogError(ex, "Error while processing the record {PerId}", record.PerId);
            return StatusCode(StatusCodes.Status500InternalServerError, "Internal server error!");
        }
    }


}
