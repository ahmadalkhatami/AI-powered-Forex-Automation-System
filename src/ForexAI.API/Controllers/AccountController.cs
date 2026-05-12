using ForexAI.Application.UseCases.GetAccountHealth;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace ForexAI.API.Controllers;

[ApiController]
[Route("api/account")]
public class AccountController : ControllerBase
{
    private readonly IMediator _mediator;

    public AccountController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<ActionResult<AccountHealthResult>> Get(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetAccountHealthQuery(), ct);
        return Ok(result);
    }
}
