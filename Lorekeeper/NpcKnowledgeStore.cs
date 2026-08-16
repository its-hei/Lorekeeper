using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Lorekeeper;

public enum KnowledgeSource
{
    Unknown,
    InferredFromDialogue,
    Manual,
    GameData
}

public sealed record NpcKnowledgeEntry(
    uint BaseId,
    string Name,
    PlayerSex Sex,
    KnowledgeSource SexSource,
    double SexConfidence);

public sealed class NpcKnowledgeStore
{
    private readonly string filePath;
    private readonly ILorekeeperLogger logger;
    private readonly object sync = new();

    private Dictionary<uint, NpcKnowledgeEntry> entries =
        new();

    public NpcKnowledgeStore(
        string filePath,
        ILorekeeperLogger logger)
    {
        this.filePath = filePath
            ?? throw new ArgumentNullException(nameof(filePath));

        this.logger = logger
            ?? throw new ArgumentNullException(nameof(logger));

        Load();
    }

    public bool TryGet(
        uint baseId,
        out NpcKnowledgeEntry entry)
    {
        if (baseId == 0)
        {
            entry = null!;
            return false;
        }

        lock (sync)
        {
            return entries.TryGetValue(
                baseId,
                out entry!);
        }
    }

    public PlayerSex GetSex(uint baseId)
    {
        return TryGet(baseId, out NpcKnowledgeEntry entry)
            ? entry.Sex
            : PlayerSex.Unknown;
    }

    public bool RememberSex(
        uint baseId,
        string npcName,
        PlayerSex sex,
        KnowledgeSource source,
        double confidence)
    {
        if (baseId == 0
            || string.IsNullOrWhiteSpace(npcName)
            || sex == PlayerSex.Unknown)
        {
            return false;
        }

        confidence = Math.Clamp(confidence, 0.0, 1.0);

        lock (sync)
        {
            if (entries.TryGetValue(
                    baseId,
                    out NpcKnowledgeEntry? existing)
                && !ShouldReplace(
                    existing,
                    sex,
                    source,
                    confidence))
            {
                return false;
            }

            entries[baseId] = new NpcKnowledgeEntry(
                baseId,
                npcName.Trim(),
                sex,
                source,
                confidence);

            SaveLocked();
            return true;
        }
    }

    private static bool ShouldReplace(
        NpcKnowledgeEntry existing,
        PlayerSex newSex,
        KnowledgeSource newSource,
        double newConfidence)
    {
        if (existing.Sex == newSex)
        {
            return GetSourcePriority(newSource)
                    > GetSourcePriority(existing.SexSource)
                || newConfidence > existing.SexConfidence;
        }

        int existingPriority =
            GetSourcePriority(existing.SexSource);

        int newPriority =
            GetSourcePriority(newSource);

        if (newPriority != existingPriority)
        {
            return newPriority > existingPriority;
        }

        return newConfidence > existing.SexConfidence;
    }

    private static int GetSourcePriority(
        KnowledgeSource source)
    {
        return source switch
        {
            KnowledgeSource.GameData => 300,
            KnowledgeSource.Manual => 200,
            KnowledgeSource.InferredFromDialogue => 100,
            _ => 0
        };
    }

    private void Load()
    {
        lock (sync)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    entries = new Dictionary<uint, NpcKnowledgeEntry>();
                    return;
                }

                string json =
                    File.ReadAllText(filePath);

                List<NpcKnowledgeEntry>? loaded =
                    JsonSerializer.Deserialize<
                        List<NpcKnowledgeEntry>>(json);

                entries =
                    (loaded ?? new List<NpcKnowledgeEntry>())
                    .Where(entry =>
                        entry.BaseId != 0
                        && !string.IsNullOrWhiteSpace(entry.Name))
                    .GroupBy(entry => entry.BaseId)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Last());

                logger.Information(
                    $"KNOWLEDGE: Wczytano {entries.Count} NPC.");
            }
            catch (Exception exception)
            {
                entries = new Dictionary<uint, NpcKnowledgeEntry>();

                logger.Error(
                    exception,
                    "KNOWLEDGE: Nie udało się wczytać knowledge.json.");
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

            List<NpcKnowledgeEntry> snapshot =
                entries.Values
                    .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(entry => entry.BaseId)
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
                "KNOWLEDGE: Nie udało się zapisać knowledge.json.");
        }
    }
}
