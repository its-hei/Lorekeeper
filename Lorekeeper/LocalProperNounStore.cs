using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Lorekeeper;

public sealed record ProtectedProperNounText(
    string Text,
    IReadOnlyDictionary<string, string> Replacements);

public sealed class LocalProperNounStore
{
    private readonly string filePath;
    private readonly ILorekeeperLogger logger;
    private readonly object sync = new();

    private List<string> entries = new();
    private DateTime lastWriteTimeUtc = DateTime.MinValue;

    public LocalProperNounStore(
        string filePath,
        ILorekeeperLogger logger)
    {
        this.filePath = filePath
            ?? throw new ArgumentNullException(nameof(filePath));

        this.logger = logger
            ?? throw new ArgumentNullException(nameof(logger));

        ReloadIfNeeded(force: true);
    }

    public IReadOnlyList<string> GetMatches(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        ReloadIfNeeded();

        lock (sync)
        {
            return entries
                .Where(entry =>
                    text.IndexOf(
                        entry,
                        StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderByDescending(entry => entry.Length)
                .ThenBy(entry => entry, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public bool HasMatch(string text)
    {
        return GetMatches(text).Count > 0;
    }

    public string GetCacheFingerprint(string text)
    {
        IReadOnlyList<string> matches =
            GetMatches(text);

        return matches.Count == 0
            ? string.Empty
            : string.Join("|", matches);
    }

    public ProtectedProperNounText Protect(string text)
    {
        IReadOnlyList<string> matches =
            GetMatches(text);

        if (matches.Count == 0)
        {
            return new ProtectedProperNounText(
                text,
                new Dictionary<string, string>());
        }

        string protectedText = text;
        Dictionary<string, string> replacements = new();

        for (int i = 0; i < matches.Count; i++)
        {
            string name = matches[i];
            string token = $"__LKPN_{i}__";

            protectedText = ReplaceIgnoreCase(
                protectedText,
                name,
                token);

            replacements[token] = name;
        }

        return new ProtectedProperNounText(
            protectedText,
            replacements);
    }

    public static string Restore(
        string text,
        IReadOnlyDictionary<string, string> replacements)
    {
        string result = text ?? string.Empty;

        foreach ((string token, string value) in replacements)
        {
            result = result.Replace(
                token,
                value,
                StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }

    private void ReloadIfNeeded(bool force = false)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                lock (sync)
                {
                    if (force || entries.Count > 0)
                    {
                        entries = new List<string>();
                        lastWriteTimeUtc = DateTime.MinValue;
                    }
                }

                return;
            }

            DateTime currentWriteTimeUtc =
                File.GetLastWriteTimeUtc(filePath);

            lock (sync)
            {
                if (!force
                    && currentWriteTimeUtc == lastWriteTimeUtc)
                {
                    return;
                }
            }

            string json =
                File.ReadAllText(filePath);

            List<string>? loaded =
                JsonSerializer.Deserialize<List<string>>(json);

            List<string> sanitized =
                (loaded ?? new List<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(value => value.Length)
                .ThenBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();

            lock (sync)
            {
                entries = sanitized;
                lastWriteTimeUtc = currentWriteTimeUtc;
            }

            logger.Information(
                $"LOCAL PROPER NAMES: Wczytano {sanitized.Count} prywatnych nazw.");
        }
        catch (Exception exception)
        {
            logger.Error(
                exception,
                "LOCAL PROPER NAMES: Nie udało się wczytać proper-names.local.json.");
        }
    }

    private static string ReplaceIgnoreCase(
        string source,
        string oldValue,
        string newValue)
    {
        if (string.IsNullOrEmpty(source)
            || string.IsNullOrEmpty(oldValue))
        {
            return source;
        }

        int startIndex = 0;

        while (true)
        {
            int index = source.IndexOf(
                oldValue,
                startIndex,
                StringComparison.OrdinalIgnoreCase);

            if (index < 0)
            {
                return source;
            }

            source =
                source[..index]
                + newValue
                + source[(index + oldValue.Length)..];

            startIndex =
                index + newValue.Length;
        }
    }
}
