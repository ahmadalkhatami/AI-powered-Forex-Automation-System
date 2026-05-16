namespace ForexAI.Domain.Enums;

/// <summary>
/// Mode account MT5 yang sedang aktif. Backend pakai ini untuk routing storage
/// (demo data vs real data harus terpisah supaya tidak mix history/equity curve).
/// Single source of truth: MT5 account yang user login — EA lapor via tick payload.
/// </summary>
public enum TradeMode
{
    Demo,
    Real
}
