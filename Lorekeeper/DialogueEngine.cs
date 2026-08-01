using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Lorekeeper;

public sealed class DialogueEngine
{
    private static readonly TimeSpan DuplicateDialogueWindow =
        TimeSpan.FromMilliseconds(500);

    private readonly ITranslator translator;
    private readonly DialogueState dialogueState = new();
    private readonly ConcurrentDictionary<string, byte> pendingDialogues = new();
    private readonly object dialogueHistorySync = new();

    private string lastNpcName = string.Empty;
    private string lastDialogue = string.Empty;
    private PlayerSex lastPlayerSex = PlayerSex.Unknown;
    private DateTime lastDialogueTimestampUtc = DateTime.MinValue;
    private long latestTranslationRequestId;

    public DialogueEngine(ITranslator translator)
    {
        this.translator = translator
            ?? throw new ArgumentNullException(nameof(translator));
    }

    public DialogueSnapshot CurrentDialogue =>
        dialogueState.GetSnapshot();

    public void MarkOpen()
    {
        dialogueState.MarkOpen();
    }

    public void MarkClosed()
    {
        dialogueState.MarkClosed();
    }

    public bool TryProcessDialogue(
        string npcName,
        string dialogue,
        TranslationContext context,
        out Task<TranslationResult?>? completion)
    {
        completion = null;
        context ??= TranslationContext.Default;

        if (string.IsNullOrWhiteSpace(dialogue))
        {
            return false;
        }

        DateTime nowUtc = DateTime.UtcNow;

        if (IsDuplicateAndRemember(
                npcName,
                dialogue,
                context.PlayerCharacterSex,
                nowUtc))
        {
            return false;
        }

        string pendingKey = CreatePendingDialogueKey(
            npcName,
            dialogue,
            context.PlayerCharacterSex);

        if (!pendingDialogues.TryAdd(pendingKey, 0))
        {
            return false;
        }

        long requestId =
            Interlocked.Increment(ref latestTranslationRequestId);

        dialogueState.BeginTranslation(npcName);

        completion = CompleteTranslationAsync(
            npcName,
            dialogue,
            context,
            pendingKey,
            requestId);

        return true;
    }

    private async Task<TranslationResult?> CompleteTranslationAsync(
        string npcName,
        string dialogue,
        TranslationContext context,
        string pendingKey,
        long requestId)
    {
        try
        {
            TranslationResult result =
                await translator.TranslateAsync(
                    dialogue,
                    npcName,
                    context);

            if (requestId
                != Volatile.Read(ref latestTranslationRequestId))
            {
                return null;
            }

            dialogueState.SetTranslation(
                npcName,
                result.TranslatedText);

            return result;
        }
        finally
        {
            pendingDialogues.TryRemove(pendingKey, out _);
        }
    }

    private bool IsDuplicateAndRemember(
        string npcName,
        string dialogue,
        PlayerSex playerSex,
        DateTime timestampUtc)
    {
        lock (dialogueHistorySync)
        {
            bool isDuplicate =
                string.Equals(
                    npcName,
                    lastNpcName,
                    StringComparison.Ordinal)
                && string.Equals(
                    dialogue,
                    lastDialogue,
                    StringComparison.Ordinal)
                && playerSex == lastPlayerSex
                && timestampUtc - lastDialogueTimestampUtc
                < DuplicateDialogueWindow;

            if (isDuplicate)
            {
                return true;
            }

            lastNpcName = npcName;
            lastDialogue = dialogue;
            lastPlayerSex = playerSex;
            lastDialogueTimestampUtc = timestampUtc;

            return false;
        }
    }

    private static string CreatePendingDialogueKey(
        string npcName,
        string dialogue,
        PlayerSex playerSex)
    {
        return $"{npcName}\u001F{dialogue}\u001F{playerSex}";
    }
}
