using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Lorekeeper.Dalamud;

internal sealed class TalkSubtitleReader
{
    private const string AddonName = "TalkSubtitle";

    private readonly IGameGui gameGui;

    public TalkSubtitleReader(IGameGui gameGui)
    {
        this.gameGui = gameGui;
    }

    public unsafe TalkSubtitleSnapshot Read()
    {
        nint addonAddress =
            gameGui.GetAddonByName(AddonName, 1);

        if (addonAddress == nint.Zero)
        {
            return TalkSubtitleSnapshot.Empty;
        }

        AtkUnitBase* addon =
            (AtkUnitBase*)addonAddress;

        if (addon == null || !addon->IsVisible)
        {
            return TalkSubtitleSnapshot.Empty;
        }

        var texts = new List<string>();

        ushort nodeCount =
            addon->UldManager.NodeListCount;

        AtkResNode** nodeList =
            addon->UldManager.NodeList;

        if (nodeList == null)
        {
            return new TalkSubtitleSnapshot(
                true,
                string.Empty,
                string.Empty);
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

            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (!texts.Contains(
                    text,
                    StringComparer.Ordinal))
            {
                texts.Add(text);
            }
        }

        if (texts.Count == 0)
        {
            return new TalkSubtitleSnapshot(
                true,
                string.Empty,
                string.Empty);
        }

        // W tym addonie treść kwestii jest zwykle najdłuższym TextNode.
        string dialogue = texts
            .OrderByDescending(text => text.Length)
            .First();

        // Nazwa postaci jest zwykle krótszym, odrębnym TextNode.
        string speaker = texts
            .Where(text =>
                !string.Equals(
                    text,
                    dialogue,
                    StringComparison.Ordinal))
            .Where(text => text.Length <= 80)
            .OrderBy(text => text.Length)
            .FirstOrDefault()
            ?? string.Empty;

        return new TalkSubtitleSnapshot(
            true,
            speaker,
            dialogue);
    }

    private static string CleanUiText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return text
            .Replace(
                "\r",
                " ",
                StringComparison.Ordinal)
            .Replace(
                "\n",
                " ",
                StringComparison.Ordinal)
            .Trim();
    }
}

internal readonly record struct TalkSubtitleSnapshot(
    bool IsVisible,
    string Speaker,
    string Dialogue)
{
    public static TalkSubtitleSnapshot Empty { get; } =
        new(
            false,
            string.Empty,
            string.Empty);
}
