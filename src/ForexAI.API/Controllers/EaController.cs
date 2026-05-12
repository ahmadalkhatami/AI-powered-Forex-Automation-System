using ForexAI.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace ForexAI.API.Controllers;

[ApiController]
[Route("api/ea")]
public class EaController : ControllerBase
{
    private readonly EaDeployService _deploy;
    private readonly ILogger<EaController> _logger;

    public EaController(EaDeployService deploy, ILogger<EaController> logger)
    {
        _deploy = deploy;
        _logger = logger;
    }

    /// <summary>
    /// Menyalin ForexAI_Bridge.mq5 terbaru ke MT5 dan mengompilasi otomatis.
    /// Dipanggil dari dashboard dengan satu tombol.
    /// </summary>
    [HttpPost("deploy")]
    public async Task<IActionResult> Deploy(CancellationToken ct)
    {
        _logger.LogInformation("EA deploy diminta dari dashboard");
        var result = await _deploy.DeployAsync(ct);

        return Ok(new
        {
            success      = result.Success,
            message      = result.Message,
            compiled     = result.Compiled,
            deployedPath = result.DeployedPath,
            compileLog   = result.CompileLog,
        });
    }
}
