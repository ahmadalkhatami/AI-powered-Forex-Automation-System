using MediatR;

namespace ForexAI.Application.UseCases.GetAdaptiveStats;

/// <param name="WindowSize">Rolling window size — default 30 trade terakhir. Set 0 = pakai semua history.</param>
public record GetAdaptiveStatsQuery(int WindowSize = 30) : IRequest<AdaptiveStatsResult>;
