using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Lorekeeper.Dalamud;

internal sealed class WideTextReader
{
    private const string AddonName = "_WideText";

    private readonly IGameGui gameGui;

    public WideTextReader(IGameGui gameGui)
    {
        this.gameGui = gameGui;
    }

    public unsafe WideTextSnapshot Read()
    {
        nint addonAddress =
            gameGui.GetAddonByName(AddonName, 1);

        if (addonAddress == nint.Zero)
        {
            return WideTextSnapshot.Empty;
        }

        AtkUnitBase* addon =
            (AtkUnitBase*)addonAddress;

        if (addon == null || !addon->IsVisible)
        {
            return WideTextSnapshot.Empty;
        }

        var texts = new List<string>();

        ushort nodeCount =
            addon->UldManager.NodeListCount;

        AtkResNode** nodeList =
            addon->UldManager.NodeList;

        if (nodeList == null)
        {
            return new WideTextSnapshot(true, string.Empty);
        }

        for (ushort index = 0; index < nodeCount; index++)
        {
            AtkResNode* node = nodeList[index];

            if (node == null
                || node->Type != NodeType.Text
                || !node->IsVisible())
            {
                continue;
            }

            AtkTextNode* textNode =
                (AtkTextNode*)node;

            string text = CleanUiText(
                textNode->NodeText.ToString());

            if (string.IsNullOrWhiteSpace(text)
                || !ContainsLetter(text))
            {
                continue;
            }

            if (!texts.Contains(text, StringComparer.Ordinal))
            {
                texts.Add(text);
            }
        }

        if (texts.Count == 0)
        {
            return new WideTextSnapshot(true, string.Empty);
        }

        // _WideText jest używany do ekranowych plansz / ScreenInfo. Główna
        // treść jest zwykle najdłuższym widocznym TextNode; pomijamy drobne
        // techniczne/ozdobne pola addonu.
        string dialogue = texts
            .OrderByDescending(text => text.Length)
            .First();

        // _WideText jest współdzielony przez fabularne plansze oraz część
        // technicznych komunikatów UI. Lorekeeper ma tłumaczyć narrację,
        // ale nie ostrzeżenia interfejsu (np. instant portrait / Active Help).
        // Filtr jest celowo konserwatywny: rozpoznajemy tylko znane,
        // jednoznacznie systemowe komunikaty zamiast odrzucać każdy krótki
        // lub "technicznie" brzmiący tekst.
        if (IsNonNarrativeSystemMessage(dialogue))
        {
            return new WideTextSnapshot(true, string.Empty);
        }

        return new WideTextSnapshot(true, dialogue);
    }

    private static bool IsNonNarrativeSystemMessage(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string value = text.Trim();

        if (value.Contains(
                "portrait reverted to default",
                StringComparison.OrdinalIgnoreCase)
            || value.Contains(
                "current appearance and/or gear does not match",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (value.Contains(
                "Active Help",
                StringComparison.OrdinalIgnoreCase)
            && (value.Contains(
                    "entry",
                    StringComparison.OrdinalIgnoreCase)
                || value.Contains(
                    "added",
                    StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    private static bool ContainsLetter(string text)
    {
        foreach (char character in text)
        {
            if (char.IsLetter(character))
            {
                return true;
            }
        }

        return false;
    }

    private static string CleanUiText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string cleaned = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();

        // Część komunikatów _WideText zawiera wewnętrzne payloady SeString
        // (ikony, skróty klawiszy, formatowanie). Po zrzuceniu ich do zwykłego
        // stringa potrafią wyglądać jak "?H????...<Return>...". Translator nie
        // powinien dostawać tego technicznego ogona.
        int payloadStart =
            FindPayloadArtifactStart(cleaned);

        if (payloadStart > 0)
        {
            string visiblePrefix =
                cleaned[..payloadStart]
                    .TrimEnd(
                        ' ',
                        '\t',
                        '-',
                        ':',
                        ';');

            if (ContainsLetter(visiblePrefix))
            {
                cleaned = visiblePrefix;
            }
        }

        return cleaned;
    }

    private static int FindPayloadArtifactStart(
        string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return -1;
        }

        const int WindowLength = 14;
        const int RequiredQuestionMarks = 3;

        for (int index = 0; index < text.Length; index++)
        {
            char startCharacter = text[index];

            if (startCharacter != '?'
                && startCharacter != '\uFFFD')
            {
                continue;
            }

            int end =
                Math.Min(
                    text.Length,
                    index + WindowLength);

            int questionMarks = 0;

            for (int cursor = index; cursor < end; cursor++)
            {
                char character = text[cursor];

                if (character == '?'
                    || character == '\uFFFD')
                {
                    questionMarks++;
                }
            }

            if (questionMarks >= RequiredQuestionMarks)
            {
                return index;
            }
        }

        return -1;
    }
}

internal readonly record struct WideTextSnapshot(
    bool IsVisible,
    string Dialogue)
{
    public static WideTextSnapshot Empty { get; } =
        new(false, string.Empty);
}
