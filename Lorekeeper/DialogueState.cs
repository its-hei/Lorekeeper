namespace Lorekeeper;

public sealed class DialogueState
{
    private readonly object syncRoot = new();
    private DialogueSnapshot snapshot = DialogueSnapshot.Empty;

    public DialogueSnapshot GetSnapshot()
    {
        lock (syncRoot)
        {
            return snapshot;
        }
    }

    public void MarkOpen()
    {
        lock (syncRoot)
        {
            snapshot = snapshot with { IsOpen = true };
        }
    }

    public void BeginTranslation(string npcName)
    {
        lock (syncRoot)
        {
            snapshot = new DialogueSnapshot(
                npcName,
                string.Empty,
                IsOpen: true);
        }
    }

    public void SetTranslation(
        string npcName,
        string translatedText)
    {
        string normalizedText =
            TranslationTextNormalizer.Normalize(translatedText);

        lock (syncRoot)
        {
            snapshot = new DialogueSnapshot(
                npcName,
                normalizedText,
                snapshot.IsOpen);
        }
    }

    public void MarkClosed()
    {
        lock (syncRoot)
        {
            snapshot = snapshot with { IsOpen = false };
        }
    }
}

public readonly record struct DialogueSnapshot(
    string NpcName,
    string Translation,
    bool IsOpen)
{
    public static DialogueSnapshot Empty { get; } = new(
        string.Empty,
        string.Empty,
        IsOpen: false);
}
