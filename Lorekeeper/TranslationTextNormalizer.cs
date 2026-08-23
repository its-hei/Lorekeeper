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

    public static string RemoveSpeakerPrefix(
        string? text,
        string? speakerName)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string result =
            text.Trim();

        if (string.IsNullOrWhiteSpace(speakerName))
        {
            return result;
        }

        string speaker =
            speakerName.Trim();

        if (!result.StartsWith(
                speaker,
                System.StringComparison.OrdinalIgnoreCase))
        {
            return result;
        }

        int separatorIndex =
            speaker.Length;

        while (separatorIndex < result.Length
               && char.IsWhiteSpace(result[separatorIndex]))
        {
            separatorIndex++;
        }

        if (separatorIndex >= result.Length
            || (result[separatorIndex] != ':'
                && result[separatorIndex] != '\uFF1A'))
        {
            return result;
        }

        separatorIndex++;

        while (separatorIndex < result.Length
               && char.IsWhiteSpace(result[separatorIndex]))
        {
            separatorIndex++;
        }

        return separatorIndex < result.Length
            ? result[separatorIndex..].TrimStart()
            : string.Empty;
    }

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
