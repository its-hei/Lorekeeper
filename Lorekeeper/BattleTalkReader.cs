using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Lorekeeper.Dalamud;

internal sealed class BattleTalkReader
{
    private const string AddonName = "_BattleTalk";

    private readonly IGameGui gameGui;

    public BattleTalkReader(IGameGui gameGui)
    {
        this.gameGui = gameGui;
    }

    public unsafe bool IsSurfaceVisible()
    {
        nint addonAddress =
            gameGui.GetAddonByName(AddonName, 1);

        if (addonAddress == nint.Zero)
        {
            return false;
        }

        AtkUnitBase* addon =
            (AtkUnitBase*)addonAddress;

        return addon != null
               && addon->IsVisible;
    }

    public unsafe BattleTalkSnapshot Read()
    {
        nint addonAddress =
            gameGui.GetAddonByName(AddonName, 1);

        if (addonAddress == nint.Zero)
        {
            return BattleTalkSnapshot.Empty;
        }

        AtkUnitBase* addon =
            (AtkUnitBase*)addonAddress;

        if (addon == null || !addon->IsVisible)
        {
            return BattleTalkSnapshot.Empty;
        }

        var entries = new List<BattleTalkTextEntry>();

        ushort nodeCount =
            addon->UldManager.NodeListCount;

        AtkResNode** nodeList =
            addon->UldManager.NodeList;

        if (nodeList == null)
        {
            return new BattleTalkSnapshot(
                true,
                string.Empty,
                string.Empty,
                false,
                0.0f,
                0.0f);
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

            if (entries.Any(entry =>
                    string.Equals(
                        entry.Text,
                        text,
                        StringComparison.Ordinal)))
            {
                continue;
            }

            float scaleX = MathF.Abs(node->ScaleX);

            if (scaleX < 0.01f)
            {
                scaleX = 1.0f;
            }

            float centerX =
                node->ScreenX
                + node->Width * scaleX * 0.5f;

            float topY = node->ScreenY;

            bool hasScreenAnchor =
                float.IsFinite(centerX)
                && float.IsFinite(topY)
                && centerX > 0.0f
                && topY > 0.0f;

            entries.Add(
                new BattleTalkTextEntry(
                    text,
                    centerX,
                    topY,
                    hasScreenAnchor));
        }

        if (entries.Count == 0)
        {
            return new BattleTalkSnapshot(
                true,
                string.Empty,
                string.Empty,
                false,
                0.0f,
                0.0f);
        }

        // _BattleTalk zawiera przede wszystkim krótką kwestię NPC.
        // Najdłuższy widoczny TextNode jest najbezpieczniejszym kandydatem
        // na sam dialog. Krótsze pola mogą zawierać nazwę lub licznik czasu.
        BattleTalkTextEntry dialogueEntry = entries
            .OrderByDescending(entry => entry.Text.Length)
            .First();

        string dialogue = dialogueEntry.Text;

        string speaker = entries
            .Where(entry =>
                !string.Equals(
                    entry.Text,
                    dialogue,
                    StringComparison.Ordinal))
            .Select(entry => entry.Text)
            .Where(text => text.Length <= 80)
            .Where(ContainsLetter)
            .OrderByDescending(text => text.Length)
            .FirstOrDefault()
            ?? string.Empty;

        return new BattleTalkSnapshot(
            true,
            speaker,
            dialogue,
            dialogueEntry.HasScreenAnchor,
            dialogueEntry.CenterX,
            dialogueEntry.TopY);
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

    private readonly record struct BattleTalkTextEntry(
        string Text,
        float CenterX,
        float TopY,
        bool HasScreenAnchor);
}

internal readonly record struct BattleTalkSnapshot(
    bool IsVisible,
    string Speaker,
    string Dialogue,
    bool HasScreenAnchor,
    float AnchorCenterX,
    float AnchorTopY)
{
    public static BattleTalkSnapshot Empty { get; } =
        new(
            false,
            string.Empty,
            string.Empty,
            false,
            0.0f,
            0.0f);
}
