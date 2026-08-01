using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using System.Linq;

namespace Lorekeeper.Dalamud;

internal static class TalkDialogueReader
{
    private const int DialogueValueIndex = 0;
    private const int NpcNameValueIndex = 1;
    private const int RequiredValueCount = 2;

    public static bool TryRead(
        AddonArgs args,
        out string npcName,
        out string dialogue)
    {
        npcName = string.Empty;
        dialogue = string.Empty;

        if (args is not AddonRefreshArgs refreshArgs)
        {
            return false;
        }

        var values = refreshArgs.AtkValueEnumerable.ToArray();

        if (values.Length < RequiredValueCount)
        {
            return false;
        }

        dialogue =
            values[DialogueValueIndex].GetValue()?.ToString()?.Trim()
            ?? string.Empty;

        npcName =
            values[NpcNameValueIndex].GetValue()?.ToString()?.Trim()
            ?? string.Empty;

        return !string.IsNullOrWhiteSpace(dialogue);
    }
}
