using ForexAI.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ForexAI.API.Controllers;

[ApiController]
[Route("api/audit")]
public class AuditController : ControllerBase
{
    private readonly AuditLogger _audit;

    public AuditController(AuditLogger audit) => _audit = audit;

    [HttpGet]
    public IActionResult Get([FromQuery] int limit = 200, [FromQuery] string? type = null)
    {
        var events = _audit.Read(limit, type);
        return Ok(events);
    }
}
