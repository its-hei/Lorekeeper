namespace Lorekeeper;

public sealed class TranslationResult
{
    public string OriginalText { get; init; } = string.Empty;

    public string TranslatedText { get; init; } = string.Empty;

    public bool FromCache { get; init; }

    public int InputTokens { get; init; }

    public int OutputTokens { get; init; }

    public decimal CostUsd { get; init; }
}
