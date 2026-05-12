namespace ForexAI.API.Models;

public record ClosePositionRequest(
    string Outcome,     // "WIN" or "LOSS"
    decimal ExitPrice
);
