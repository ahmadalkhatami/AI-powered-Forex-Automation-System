using ForexAI.Domain.Entities;
using MediatR;

namespace ForexAI.Application.UseCases.GetPositionStatus;

public record GetPositionStatusQuery(string Pair) : IRequest<TradePosition?>;
