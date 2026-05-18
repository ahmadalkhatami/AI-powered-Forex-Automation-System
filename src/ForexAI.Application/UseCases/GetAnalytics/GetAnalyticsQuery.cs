using MediatR;

namespace ForexAI.Application.UseCases.GetAnalytics;

public record GetAnalyticsQuery() : IRequest<AnalyticsResult>;
