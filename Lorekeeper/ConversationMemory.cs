using System;
using System.Collections.Generic;
using System.Linq;

namespace Lorekeeper;

public sealed record ConversationLine(
    string Speaker,
    string OriginalText,
    string TranslatedText);

public sealed class ConversationMemory
{
    private readonly object sync = new();
    private readonly int capacity;

    private readonly Queue<ConversationLine> lines = new();

    public ConversationMemory(int capacity = 20)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                capacity,
                "Capacity must be greater than zero.");
        }

        this.capacity = capacity;
    }

    public void Add(
        string speaker,
        string originalText,
        string translatedText)
    {
        if (string.IsNullOrWhiteSpace(originalText))
        {
            return;
        }

        ConversationLine line = new(
            NormalizeSpeaker(speaker),
            originalText.Trim(),
            translatedText?.Trim() ?? string.Empty);

        lock (sync)
        {
            lines.Enqueue(line);

            while (lines.Count > capacity)
            {
                lines.Dequeue();
            }
        }
    }

    public IReadOnlyList<ConversationLine> GetRecent()
    {
        lock (sync)
        {
            return lines.ToList();
        }
    }

    public IReadOnlyList<ConversationLine> GetRecentForPrompt(
        int maxLines = 5)
    {
        if (maxLines <= 0)
        {
            return Array.Empty<ConversationLine>();
        }

        lock (sync)
        {
            int skip =
                Math.Max(0, lines.Count - maxLines);

            return lines
                .Skip(skip)
                .ToList();
        }
    }

    public void Clear()
    {
        lock (sync)
        {
            lines.Clear();
        }
    }

    public int Count
    {
        get
        {
            lock (sync)
            {
                return lines.Count;
            }
        }
    }

    private static string NormalizeSpeaker(
        string speaker)
    {
        return string.IsNullOrWhiteSpace(speaker)
            ? "???"
            : speaker.Trim();
    }
}
