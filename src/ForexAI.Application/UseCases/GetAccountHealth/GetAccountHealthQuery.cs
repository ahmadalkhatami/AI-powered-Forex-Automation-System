using MediatR;

namespace ForexAI.Application.UseCases.GetAccountHealth;

public record GetAccountHealthQuery : IRequest<AccountHealthResult>;
