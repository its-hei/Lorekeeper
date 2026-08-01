using System;
using System.IO;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Windowing;

namespace Lorekeeper.Windows;

public sealed class MainWindow : Window, IDisposable
{
    private const float WindowWidth = 760.0f;
    private const float WindowBottomMargin = 190.0f;

    private const float HorizontalPadding = 28.0f;
    private const float VerticalPadding = 15.0f;
    private const float WindowRounding = 34.0f;
    private const float WindowBorderSize = 1.0f;
    private const float SpeakerSpacing = 7.0f;

    private const float DialogueFontSize = 20.0f;
    private const float NpcNameFontSize = 22.0f;

    private const float FadeInSpeed = 8.0f;
    private const float FadeOutSpeed = 5.0f;
    private const float InvisibleAlphaThreshold = 0.001f;

    private readonly Plugin plugin;
    private readonly IFontHandle dialogueFont;
    private readonly IFontHandle npcNameFont;

    private DialogueSnapshot currentDialogue = DialogueSnapshot.Empty;
    private float fadeAlpha;

    public MainWindow(Plugin plugin)
        : base("Lorekeeper###LorekeeperDialogueOverlayAnchorV2")
    {
        this.plugin = plugin;

        string pluginDirectory =
            Plugin.PluginInterface.AssemblyLocation.Directory?.FullName
            ?? Plugin.PluginInterface.ConfigDirectory.FullName;

        string fontPath = Path.Combine(
            pluginDirectory,
            "Assets",
            "Fonts",
            "NotoSans-SemiBold.ttf");

        dialogueFont = CreateFontHandle(fontPath, DialogueFontSize);
        npcNameFont = CreateFontHandle(fontPath, NpcNameFontSize);

        Flags =
            ImGuiWindowFlags.NoDecoration |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoFocusOnAppearing |
            ImGuiWindowFlags.NoNav |
            ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse |
            ImGuiWindowFlags.NoInputs |
            ImGuiWindowFlags.AlwaysAutoResize;

        IsOpen = false;
    }

    public void Dispose()
    {
        dialogueFont.Dispose();
        npcNameFont.Dispose();
    }

    public override void PreDraw()
    {
        currentDialogue = plugin.CurrentDialogue;
        UpdateFade();
        ApplyWindowLayout();
        PushWindowStyle();
    }

    public override void Draw()
    {
        if (!ShouldDrawContent())
        {
            return;
        }

        float availableTextWidth =
            WindowWidth - HorizontalPadding * 2.0f;

        ImGui.PushTextWrapPos(
            ImGui.GetCursorPosX() + availableTextWidth);

        DrawNpcName();

        ImGui.Dummy(new Vector2(0.0f, SpeakerSpacing));

        DrawTranslation();

        ImGui.PopTextWrapPos();
    }

    public override void PostDraw()
    {
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar(3);
    }

    private static IFontHandle CreateFontHandle(
        string fontPath,
        float fontSize)
    {
        return Plugin.PluginInterface.UiBuilder.FontAtlas
            .NewDelegateFontHandle(
                buildStep =>
                    buildStep.OnPreBuild(
                        toolkit =>
                        {
                            if (File.Exists(fontPath))
                            {
                                toolkit.AddFontFromFile(
                                    fontPath,
                                    new SafeFontConfig
                                    {
                                        SizePx = fontSize,
                                        GlyphRanges =
                                        [
                                            0x0020,
                                            0x00FF,
                                            0x0100,
                                            0x017F,
                                            0x2000,
                                            0x206F,
                                            0
                                        ]
                                    });

                                Plugin.Log.Information(
                                    $"Załadowano font {fontSize}px: {fontPath}");

                                return;
                            }

                            Plugin.Log.Warning(
                                $"Nie znaleziono fontu: {fontPath}");

                            toolkit.AddDalamudDefaultFont(fontSize);
                        }));
    }

    private void ApplyWindowLayout()
    {
        var viewport = ImGui.GetMainViewport();

        Vector2 windowPosition = new(
            viewport.Pos.X + viewport.Size.X * 0.5f,
            viewport.Pos.Y + viewport.Size.Y - WindowBottomMargin);

        ImGui.SetNextWindowPos(
            windowPosition,
            ImGuiCond.Always,
            new Vector2(0.5f, 1.0f));

        ImGui.SetNextWindowSize(
            new Vector2(WindowWidth, 0.0f),
            ImGuiCond.Always);
    }

    private void PushWindowStyle()
    {
        ImGui.PushStyleVar(
            ImGuiStyleVar.WindowPadding,
            new Vector2(HorizontalPadding, VerticalPadding));

        ImGui.PushStyleVar(
            ImGuiStyleVar.WindowRounding,
            WindowRounding);

        ImGui.PushStyleVar(
            ImGuiStyleVar.WindowBorderSize,
            WindowBorderSize);

        ImGui.PushStyleColor(
            ImGuiCol.WindowBg,
            new Vector4(
                0.105f,
                0.105f,
                0.115f,
                0.76f * fadeAlpha));

        ImGui.PushStyleColor(
            ImGuiCol.Border,
            new Vector4(
                1.0f,
                1.0f,
                1.0f,
                0.18f * fadeAlpha));

        ImGui.PushStyleColor(
            ImGuiCol.Text,
            new Vector4(
                1.0f,
                1.0f,
                1.0f,
                fadeAlpha));
    }

    private bool ShouldDrawContent()
    {
        return fadeAlpha > InvisibleAlphaThreshold
               && !string.IsNullOrWhiteSpace(currentDialogue.Translation);
    }

    private void DrawNpcName()
    {
        using (npcNameFont.Push())
        {
            ImGui.TextUnformatted(currentDialogue.NpcName);
        }
    }

    private void DrawTranslation()
    {
        using (dialogueFont.Push())
        {
            ImGui.TextWrapped(currentDialogue.Translation);
        }
    }

    private void UpdateFade()
    {
        float targetAlpha = currentDialogue.IsOpen
            ? 1.0f
            : 0.0f;

        float speed = targetAlpha > fadeAlpha
            ? FadeInSpeed
            : FadeOutSpeed;

        fadeAlpha = MoveTowards(
            fadeAlpha,
            targetAlpha,
            speed * ImGui.GetIO().DeltaTime);

        if (currentDialogue.IsOpen
            || fadeAlpha > InvisibleAlphaThreshold)
        {
            return;
        }

        fadeAlpha = 0.0f;
        IsOpen = false;
    }

    private static float MoveTowards(
        float current,
        float target,
        float maximumDelta)
    {
        float difference = target - current;

        if (MathF.Abs(difference) <= maximumDelta)
        {
            return target;
        }

        return current
               + MathF.Sign(difference) * maximumDelta;
    }
}
