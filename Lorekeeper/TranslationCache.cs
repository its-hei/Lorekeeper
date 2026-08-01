using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Lorekeeper;

public sealed class TranslationCache
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly object syncRoot = new();
    private readonly string filePath;
    private readonly Dictionary<string, string> translations =
        new(StringComparer.Ordinal);

    public TranslationCache(string filePath)
    {
        this.filePath = filePath;
        Load();
    }

    public bool TryGet(string originalText, out string translatedText)
    {
        lock (syncRoot)
        {
            return translations.TryGetValue(originalText, out translatedText!);
        }
    }

    public void Add(string originalText, string translatedText)
    {
        lock (syncRoot)
        {
            if (translations.TryGetValue(originalText, out string? existing)
                && string.Equals(existing, translatedText, StringComparison.Ordinal))
            {
                return;
            }

            translations[originalText] = translatedText;
            Save();
        }
    }

    private void Load()
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        try
        {
            string json = File.ReadAllText(filePath);
            Dictionary<string, string>? loaded =
                JsonSerializer.Deserialize<Dictionary<string, string>>(json);

            if (loaded is null)
            {
                return;
            }

            foreach (KeyValuePair<string, string> entry in loaded)
            {
                translations[entry.Key] = entry.Value;
            }
        }
        catch (JsonException)
        {
            translations.Clear();
        }
        catch (IOException)
        {
            translations.Clear();
        }
        catch (UnauthorizedAccessException)
        {
            translations.Clear();
        }
    }

    private void Save()
    {
        string? directory = Path.GetDirectoryName(filePath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = filePath + ".tmp";
        string json = JsonSerializer.Serialize(translations, SerializerOptions);

        try
        {
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
