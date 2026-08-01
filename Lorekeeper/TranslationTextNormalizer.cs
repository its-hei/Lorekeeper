using System.Text.RegularExpressions;

namespace Lorekeeper;

public static class TranslationTextNormalizer
{
    private static readonly Regex DashPattern = new(
        @"[ \t]*[\u2010-\u2015\u2212\u2500][ \t]*",
        RegexOptions.Compiled);

    private static readonly Regex LeadingDialogueDashPattern = new(
        @"^[ \t]*-[ \t]*(?!\\d)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex RepeatedHorizontalWhitespacePattern = new(
        @"[ \t]{2,}",
        RegexOptions.Compiled);

    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string normalized = text
            .Replace('\u00A0', ' ')
            .Replace("\r\n", "\n")
            .Replace('\r', '\n');

        normalized = DashPattern.Replace(normalized, " - ");
        normalized = RepeatedHorizontalWhitespacePattern.Replace(normalized, " ");
        normalized = LeadingDialogueDashPattern.Replace(normalized, string.Empty);

        return normalized.Trim();
    }
}
