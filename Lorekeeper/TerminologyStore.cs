using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Lorekeeper;

public enum TerminologySource
{
    Unknown,
    Learned,
    Manual
}

public sealed record TerminologyEntry(
    string SourceTerm,
    string PreferredTranslation,
    bool InflectForGender,
    string? FeminineForm,
    TerminologySource Source,
    double Confidence);

public sealed class TerminologyStore
{
    private readonly string filePath;
    private readonly ILorekeeperLogger logger;
    private readonly object sync = new();

    private Dictionary<string, TerminologyEntry> entries =
        new(StringComparer.OrdinalIgnoreCase);

    public TerminologyStore(
        string filePath,
        ILorekeeperLogger logger)
    {
        this.filePath = filePath
            ?? throw new ArgumentNullException(nameof(filePath));

        this.logger = logger
            ?? throw new ArgumentNullException(nameof(logger));

        Load();
        EnsureDefaults();
    }

    public IReadOnlyList<TerminologyEntry> GetAll()
    {
        lock (sync)
        {
            return entries.Values
                .OrderBy(
                    entry => entry.SourceTerm,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public bool TryGet(
        string sourceTerm,
        out TerminologyEntry entry)
    {
        if (string.IsNullOrWhiteSpace(sourceTerm))
        {
            entry = null!;
            return false;
        }

        lock (sync)
        {
            return entries.TryGetValue(
                Normalize(sourceTerm),
                out entry!);
        }
    }

    public bool Remember(
        string sourceTerm,
        string preferredTranslation,
        bool inflectForGender,
        string? feminineForm,
        TerminologySource source,
        double confidence)
    {
        if (string.IsNullOrWhiteSpace(sourceTerm)
            || string.IsNullOrWhiteSpace(preferredTranslation))
        {
            return false;
        }

        confidence = Math.Clamp(confidence, 0.0, 1.0);
        string key = Normalize(sourceTerm);

        lock (sync)
        {
            TerminologyEntry candidate = new(
                sourceTerm.Trim(),
                preferredTranslation.Trim(),
                inflectForGender,
                string.IsNullOrWhiteSpace(feminineForm)
                    ? null
                    : feminineForm.Trim(),
                source,
                confidence);

            if (entries.TryGetValue(
                    key,
                    out TerminologyEntry? existing)
                && !ShouldReplace(existing, candidate))
            {
                return false;
            }

            entries[key] = candidate;
            SaveLocked();
            return true;
        }
    }

    private static bool ShouldReplace(
        TerminologyEntry existing,
        TerminologyEntry candidate)
    {
        int existingPriority =
            GetSourcePriority(existing.Source);

        int candidatePriority =
            GetSourcePriority(candidate.Source);

        if (candidatePriority != existingPriority)
        {
            return candidatePriority > existingPriority;
        }

        return candidate.Confidence > existing.Confidence;
    }

    private static int GetSourcePriority(
        TerminologySource source)
    {
        return source switch
        {
            TerminologySource.Manual => 200,
            TerminologySource.Learned => 100,
            _ => 0
        };
    }

    private void EnsureDefaults()
    {
        bool changed = false;

        TerminologyEntry[] defaults =
        [
            new(
                "adventurer",
                "poszukiwacz przygód",
                false,
                null,
                TerminologySource.Manual,
                1.0),
            new(
                "Free Company",
                "Wolna Kompania",
                false,
                null,
                TerminologySource.Manual,
                1.0),
            new(
                "Grand Company",
                "Wielka Kompania",
                false,
                null,
                TerminologySource.Manual,
                1.0),
            new(
                "Warrior of Light",
                "Wojownik Światła",
                false,
                null,
                TerminologySource.Manual,
                1.0)
        ];

        lock (sync)
        {
            foreach (TerminologyEntry defaultEntry in defaults)
            {
                string key = Normalize(defaultEntry.SourceTerm);

                bool needsMigration =
                    !entries.TryGetValue(
                        key,
                        out TerminologyEntry? existing)
                    || !string.Equals(
                        existing.PreferredTranslation,
                        defaultEntry.PreferredTranslation,
                        StringComparison.Ordinal)
                    || existing.InflectForGender !=
                        defaultEntry.InflectForGender
                    || !string.Equals(
                        existing.FeminineForm,
                        defaultEntry.FeminineForm,
                        StringComparison.Ordinal);

                if (!needsMigration)
                {
                    continue;
                }

                entries[key] = defaultEntry;
                changed = true;
            }

            if (changed)
            {
                SaveLocked();
            }
        }
    }

    private bool RememberWithoutSaving(
        TerminologyEntry candidate)
    {
        string key = Normalize(candidate.SourceTerm);

        lock (sync)
        {
            if (entries.TryGetValue(
                    key,
                    out TerminologyEntry? existing)
                && !ShouldReplace(existing, candidate))
            {
                return false;
            }

            entries[key] = candidate;
            return true;
        }
    }

    private void Load()
    {
        lock (sync)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    entries =
                        new Dictionary<string, TerminologyEntry>(
                            StringComparer.OrdinalIgnoreCase);

                    return;
                }

                string json =
                    File.ReadAllText(filePath);

                List<TerminologyEntry>? loaded =
                    JsonSerializer.Deserialize<
                        List<TerminologyEntry>>(json);

                entries =
                    (loaded ?? new List<TerminologyEntry>())
                    .Where(entry =>
                        !string.IsNullOrWhiteSpace(entry.SourceTerm)
                        && !string.IsNullOrWhiteSpace(
                            entry.PreferredTranslation))
                    .ToDictionary(
                        entry => Normalize(entry.SourceTerm),
                        entry => entry,
                        StringComparer.OrdinalIgnoreCase);

                logger.Information(
                    $"TERMINOLOGY: Wczytano {entries.Count} terminów.");
            }
            catch (Exception exception)
            {
                entries =
                    new Dictionary<string, TerminologyEntry>(
                        StringComparer.OrdinalIgnoreCase);

                logger.Error(
                    exception,
                    "TERMINOLOGY: Nie udało się wczytać terminology.json.");
            }
        }
    }

    private void SaveLocked()
    {
        try
        {
            string? directory =
                Path.GetDirectoryName(filePath);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            List<TerminologyEntry> snapshot =
                entries.Values
                    .OrderBy(
                        entry => entry.SourceTerm,
                        StringComparer.OrdinalIgnoreCase)
                    .ToList();

            string json =
                JsonSerializer.Serialize(
                    snapshot,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

            string temporaryPath =
                filePath + ".tmp";

            File.WriteAllText(
                temporaryPath,
                json);

            File.Move(
                temporaryPath,
                filePath,
                overwrite: true);
        }
        catch (Exception exception)
        {
            logger.Error(
                exception,
                "TERMINOLOGY: Nie udało się zapisać terminology.json.");
        }
    }

    private static string Normalize(
        string sourceTerm)
    {
        return sourceTerm.Trim();
    }
}
