using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;

namespace Lorekeeper.Windows;

public sealed class MainWindow : Window, IDisposable
{
    private const float WindowWidth = 760.0f;
    private const float RelicWindowWidth = 700.0f;
    private const float HudWindowWidth = 720.0f;

    private const float WindowBottomMargin = 190.0f;
    private const float CoverOriginalBottomMargin = 34.0f;
    private const float BattleTalkGap = 30.0f;

    private const float CinematicWindowWidth = 1000.0f;
    private const float CinematicTextWidth = 900.0f;
    private const float CinematicBottomMargin = 170.0f;
    private const float CinematicLineSpacing = 6.0f;

    private const float HorizontalPadding = 28.0f;
    private const float VerticalPadding = 15.0f;
    private const float WindowRounding = 34.0f;
    private const float WindowBorderSize = 1.0f;
    private const float SpeakerSpacing = 7.0f;

    private const float DialogueFontSize = 20.0f;
    private const float NpcNameFontSize = 22.0f;
    private const float CinematicFontSize = 30.0f;

    private const float FadeInSpeed = 8.0f;
    private const float FadeOutSpeed = 5.0f;
    private const float InvisibleAlphaThreshold = 0.001f;

    private readonly Plugin plugin;
    private readonly IFontHandle dialogueFont;
    private readonly IFontHandle npcNameFont;
    private readonly IFontHandle cinematicFont;
    private readonly IFontHandle styledDialogueFont;
    private readonly IFontHandle styledNpcNameFont;
    private readonly ISharedImmediateTexture? relicFrameTexture;
    private readonly ISharedImmediateTexture? hudFrameTexture;

    private static readonly DialogueSnapshot NormalPreviewDialogue = new(
        "Y'shtola",
        "To jest przykładowe tłumaczenie dialogu. Tak będzie wyglądał tekst podczas rozmowy z NPC.",
        IsOpen: true);

    private static readonly DialogueSnapshot CinematicPreviewDialogue = new(
        string.Empty,
        "To jest przykładowe tłumaczenie napisu cinematic.",
        IsOpen: true);

    private DialogueSnapshot currentDialogue = DialogueSnapshot.Empty;
    private float fadeAlpha;
    private bool previewActive;
    private DialogueDisplayKind previewDisplayKind =
        DialogueDisplayKind.Normal;

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

        string cinematicFontPath = Path.Combine(
            pluginDirectory,
            "Assets",
            "Fonts",
            "NotoSans-Regular.ttf");

        dialogueFont = CreateFontHandle(fontPath, DialogueFontSize);
        npcNameFont = CreateFontHandle(fontPath, NpcNameFontSize);
        cinematicFont = CreateFontHandle(
            cinematicFontPath,
            CinematicFontSize);
        styledDialogueFont = CreateFontHandle(fontPath, 18.0f);
        styledNpcNameFont = CreateFontHandle(fontPath, 20.0f);

        string bubbleAssetsDirectory = Path.Combine(
            pluginDirectory,
            "Assets",
            "Bubbles");

        string relicFramePath = Path.Combine(
            bubbleAssetsDirectory,
            "RelicFrame.png");

        string hudFramePath = Path.Combine(
            bubbleAssetsDirectory,
            "HudFrame.png");

        relicFrameTexture = File.Exists(relicFramePath)
            ? Plugin.TextureProvider.GetFromFile(relicFramePath)
            : null;

        hudFrameTexture = File.Exists(hudFramePath)
            ? Plugin.TextureProvider.GetFromFile(hudFramePath)
            : null;

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
        cinematicFont.Dispose();
        styledDialogueFont.Dispose();
        styledNpcNameFont.Dispose();
    }

    public void ShowPreview(
        DialogueDisplayKind displayKind)
    {
        previewDisplayKind =
            displayKind;

        previewActive = true;
        fadeAlpha = 1.0f;
        IsOpen = true;
    }

    public void HidePreview()
    {
        previewActive = false;

        if (!plugin.CurrentDialogue.IsOpen)
        {
            currentDialogue =
                DialogueSnapshot.Empty;

            fadeAlpha = 0.0f;
            IsOpen = false;
        }
    }

    public bool IsPreviewActive(
        DialogueDisplayKind displayKind)
    {
        return previewActive
               && previewDisplayKind == displayKind;
    }

    public override void PreDraw()
    {
        if (previewActive
            && (!plugin.IsConfigWindowOpen
                || plugin.CurrentDialogue.IsOpen))
        {
            previewActive = false;
        }

        if (previewActive)
        {
            currentDialogue =
                previewDisplayKind == DialogueDisplayKind.Cinematic
                    ? CinematicPreviewDialogue
                    : NormalPreviewDialogue;

            fadeAlpha = 1.0f;
        }
        else
        {
            currentDialogue =
                plugin.CurrentDialogue;

            UpdateFade();
        }

        ApplyWindowLayout();
        PushWindowStyle();
    }

    public override void Draw()
    {
        if (!ShouldDrawContent())
        {
            return;
        }

        ApplyLiveFontScale();

        if (IsCinematicDialogue())
        {
            DrawCinematicTranslation();
            return;
        }

        DrawBubbleBackdrop();

        DialogueBubbleStyle activeStyle =
            GetActiveBubbleStyle();

        float activeWindowWidth =
            GetActiveWindowWidth(
                activeStyle);

        float availableTextWidth =
            activeStyle switch
            {
                DialogueBubbleStyle.Relic =>
                    activeWindowWidth - 34.0f * 2.0f - 16.0f,

                DialogueBubbleStyle.Hud =>
                    activeWindowWidth - 32.0f * 2.0f - 62.0f,

                _ =>
                    activeWindowWidth - HorizontalPadding * 2.0f
            };

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

    private void ApplyLiveFontScale()
    {
        ImGui.SetWindowFontScale(
            IsCinematicDialogue()
                ? GetCinematicFontScale()
                : GetNormalFontScale());
    }

    private float GetNormalFontScale()
    {
        return Math.Clamp(
            plugin.Configuration.NormalDialogueFontSize
            / DialogueFontSize,
            0.70f,
            1.60f);
    }

    private float GetCinematicFontScale()
    {
        return Math.Clamp(
            plugin.Configuration.CinematicFontSize
            / CinematicFontSize,
            0.70f,
            1.60f);
    }

    private void ApplyWindowLayout()
    {
        var viewport = ImGui.GetMainViewport();

        if (IsCinematicDialogue())
        {
            Flags &=
                ~ImGuiWindowFlags.AlwaysAutoResize;

            float cinematicHeight =
                CalculateCinematicWindowHeight();

            float cinematicVerticalOffset =
                Math.Clamp(
                    plugin.Configuration.CinematicVerticalOffset,
                    -180.0f,
                    180.0f);

            Vector2 cinematicPosition = new(
                viewport.Pos.X + viewport.Size.X * 0.5f,
                viewport.Pos.Y
                + viewport.Size.Y
                - CinematicBottomMargin
                + cinematicVerticalOffset);

            ImGui.SetNextWindowPos(
                cinematicPosition,
                ImGuiCond.Always,
                new Vector2(0.5f, 1.0f));

            ImGui.SetNextWindowSize(
                new Vector2(
                    CinematicWindowWidth,
                    cinematicHeight),
                ImGuiCond.Always);

            return;
        }

        DialogueBubbleStyle style =
            GetActiveBubbleStyle();

        float activeWindowWidth =
            GetActiveWindowWidth(
                style);

        float bottomMargin =
            ShouldCoverOriginalDialogue()
                ? CoverOriginalBottomMargin
                : WindowBottomMargin;

        float normalVerticalOffset =
            Math.Clamp(
                plugin.Configuration.NormalDialogueVerticalOffset,
                -180.0f,
                180.0f);

        Vector2 windowPosition = new(
            viewport.Pos.X + viewport.Size.X * 0.5f,
            viewport.Pos.Y
            + viewport.Size.Y
            - bottomMargin
            + normalVerticalOffset);

        // _BattleTalk może stać się widoczny pomiędzy dwoma tickami naszego
        // pollera. Wtedy CurrentDialogueSource przez jedną klatkę nadal wskazuje
        // poprzednią powierzchnię i ImGui zdąży narysować jej dolne tło, zanim
        // PollBattleTalk przełączy layout na górny. Sam ShouldDrawContent() nie
        // wystarcza, bo tło okna powstaje jeszcze przed Draw().
        //
        // Dlatego sprawdzamy widoczność _BattleTalk bezpośrednio w PreDraw i
        // chowamy CAŁE okno poza viewportem zarówno podczas tej klatki przejściowej,
        // jak i wtedy, gdy znamy już source, ale nie mamy jeszcze kotwicy.
        bool pendingBattleTalkSource =
            !IsBattleTalkDialogue()
            && plugin.IsBattleTalkSurfaceVisibleNow();

        bool battleTalkWaitingForAnchor =
            IsBattleTalkDialogue()
            && !plugin.TryGetBattleTalkScreenAnchor(
                out _,
                out _);

        if (pendingBattleTalkSource
            || battleTalkWaitingForAnchor)
        {
            ImGui.SetNextWindowPos(
                new Vector2(
                    viewport.Pos.X - 10000.0f,
                    viewport.Pos.Y - 10000.0f),
                ImGuiCond.Always);

            ImGui.SetNextWindowBgAlpha(0.0f);

            if (style == DialogueBubbleStyle.Classic)
            {
                Flags |=
                    ImGuiWindowFlags.AlwaysAutoResize;

                ImGui.SetNextWindowSize(
                    new Vector2(activeWindowWidth, 0.0f),
                    ImGuiCond.Always);
            }
            else
            {
                Flags &=
                    ~ImGuiWindowFlags.AlwaysAutoResize;

                ImGui.SetNextWindowSize(
                    new Vector2(
                        activeWindowWidth,
                        CalculateStyledWindowHeight(
                            style,
                            activeWindowWidth)),
                    ImGuiCond.Always);
            }

            return;
        }

        // _BattleTalk (krótkie kwestie NPC podczas walki) ma własną bańkę gry.
        // Lorekeeper umieszczamy bezpośrednio NAD oryginalnym tekstem zamiast
        // przy dolnej krawędzi ekranu. ScreenX/ScreenY pochodzą z TextNode
        // bieżącej kwestii, więc pozycja podąża za HUD-em i skalą UI.
        if (IsBattleTalkDialogue()
            && plugin.TryGetBattleTalkScreenAnchor(
                out float battleTalkCenterX,
                out float battleTalkTopY))
        {
            float halfWidth =
                activeWindowWidth * 0.5f;

            float minimumCenterX =
                viewport.Pos.X + halfWidth + 8.0f;

            float maximumCenterX =
                viewport.Pos.X
                + viewport.Size.X
                - halfWidth
                - 8.0f;

            float anchoredCenterX =
                maximumCenterX > minimumCenterX
                    ? Math.Clamp(
                        battleTalkCenterX,
                        minimumCenterX,
                        maximumCenterX)
                    : viewport.Pos.X + viewport.Size.X * 0.5f;

            float anchoredBottomY =
                Math.Clamp(
                    battleTalkTopY
                    - BattleTalkGap
                    + normalVerticalOffset,
                    viewport.Pos.Y + 96.0f,
                    viewport.Pos.Y
                    + viewport.Size.Y
                    - 24.0f);

            windowPosition =
                new Vector2(
                    anchoredCenterX,
                    anchoredBottomY);
        }

        ImGui.SetNextWindowPos(
            windowPosition,
            ImGuiCond.Always,
            new Vector2(0.5f, 1.0f));

        if (style == DialogueBubbleStyle.Classic)
        {
            Flags |=
                ImGuiWindowFlags.AlwaysAutoResize;

            ImGui.SetNextWindowSize(
                new Vector2(activeWindowWidth, 0.0f),
                ImGuiCond.Always);

            return;
        }

        // Relikt i HUD potrzebują własnej minimalnej wysokości.
        // Przy krótkich kwestiach zwykły AlwaysAutoResize ściskał
        // ramkę do wysokości samego tekstu i wizualnie "obcinał" grafikę.
        Flags &=
            ~ImGuiWindowFlags.AlwaysAutoResize;

        float styledHeight =
            CalculateStyledWindowHeight(
                style,
                activeWindowWidth);

        ImGui.SetNextWindowSize(
            new Vector2(
                activeWindowWidth,
                styledHeight),
            ImGuiCond.Always);
    }

    private float CalculateStyledWindowHeight(
        DialogueBubbleStyle style,
        float windowWidth)
    {
        Vector2 padding =
            style == DialogueBubbleStyle.Relic
                ? new Vector2(34.0f, 20.0f)
                : new Vector2(32.0f, 20.0f);

        // W HUD zostawiamy dodatkowe miejsce po prawej
        // na ozdobny element ramki.
        float rightDecorationSpace =
            style == DialogueBubbleStyle.Hud
                ? 62.0f
                : 18.0f;

        float availableTextWidth =
            MathF.Max(
                220.0f,
                windowWidth
                - padding.X * 2.0f
                - rightDecorationSpace);

        float fontScale =
            GetNormalFontScale();

        float speakerHeight;

        using (styledNpcNameFont.Push())
        {
            speakerHeight =
                ImGui.GetTextLineHeight()
                * fontScale;
        }

        float dialogueHeight;

        using (styledDialogueFont.Push())
        {
            dialogueHeight =
                string.IsNullOrWhiteSpace(
                    currentDialogue.Translation)
                    ? ImGui.GetTextLineHeight()
                      * fontScale
                    : ImGui.CalcTextSize(
                        currentDialogue.Translation,
                        false,
                        availableTextWidth / fontScale).Y
                      * fontScale;
        }

        float calculatedHeight =
            padding.Y * 2.0f
            + speakerHeight
            + SpeakerSpacing * fontScale
            + dialogueHeight
            + 14.0f;

        float minimumHeight =
            style == DialogueBubbleStyle.Relic
                ? 126.0f
                : 124.0f;

        return MathF.Max(
            minimumHeight,
            calculatedHeight);
    }

    private void PushWindowStyle()
    {
        if (IsCinematicDialogue())
        {
            ImGui.PushStyleVar(
                ImGuiStyleVar.WindowPadding,
                Vector2.Zero);

            ImGui.PushStyleVar(
                ImGuiStyleVar.WindowRounding,
                0.0f);

            ImGui.PushStyleVar(
                ImGuiStyleVar.WindowBorderSize,
                0.0f);

            ImGui.PushStyleColor(
                ImGuiCol.WindowBg,
                new Vector4(
                    0.0f,
                    0.0f,
                    0.0f,
                    0.0f));

            ImGui.PushStyleColor(
                ImGuiCol.Border,
                new Vector4(
                    0.0f,
                    0.0f,
                    0.0f,
                    0.0f));

            ImGui.PushStyleColor(
                ImGuiCol.Text,
                new Vector4(
                    1.0f,
                    1.0f,
                    1.0f,
                    fadeAlpha));

            return;
        }

        DialogueBubbleStyle style =
            GetActiveBubbleStyle();

        float rounding =
            style == DialogueBubbleStyle.Classic
                ? WindowRounding
                : style == DialogueBubbleStyle.Relic
                    ? 16.0f
                    : 0.0f;

        float borderSize =
            style == DialogueBubbleStyle.Classic
                ? WindowBorderSize
                : 0.0f;

        float classicOpacity =
            Math.Clamp(
                plugin.Configuration.ClassicBubbleOpacity,
                0.20f,
                1.0f);

        Vector4 backgroundColor =
            style == DialogueBubbleStyle.Classic
                ? new Vector4(
                    0.105f,
                    0.105f,
                    0.115f,
                    classicOpacity * fadeAlpha)
                : new Vector4(
                    0.0f,
                    0.0f,
                    0.0f,
                    0.0f);

        Vector4 borderColor =
            style == DialogueBubbleStyle.Classic
                ? new Vector4(
                    1.0f,
                    1.0f,
                    1.0f,
                    0.18f * fadeAlpha)
                : new Vector4(
                    0.0f,
                    0.0f,
                    0.0f,
                    0.0f);

        Vector4 textColor =
            style == DialogueBubbleStyle.Relic
                ? new Vector4(
                    0.10f,
                    0.09f,
                    0.08f,
                    fadeAlpha)
                : new Vector4(
                    1.0f,
                    1.0f,
                    1.0f,
                    fadeAlpha);

        Vector2 windowPadding =
            style switch
            {
                DialogueBubbleStyle.Relic =>
                    new Vector2(34.0f, 20.0f),

                DialogueBubbleStyle.Hud =>
                    new Vector2(32.0f, 20.0f),

                _ =>
                    new Vector2(HorizontalPadding, VerticalPadding)
            };

        ImGui.PushStyleVar(
            ImGuiStyleVar.WindowPadding,
            windowPadding);

        ImGui.PushStyleVar(
            ImGuiStyleVar.WindowRounding,
            rounding);

        ImGui.PushStyleVar(
            ImGuiStyleVar.WindowBorderSize,
            borderSize);

        ImGui.PushStyleColor(
            ImGuiCol.WindowBg,
            backgroundColor);

        ImGui.PushStyleColor(
            ImGuiCol.Border,
            borderColor);

        ImGui.PushStyleColor(
            ImGuiCol.Text,
            textColor);
    }

    private void DrawBubbleBackdrop()
    {
        if (IsCinematicDialogue())
        {
            return;
        }

        DialogueBubbleStyle style =
            GetActiveBubbleStyle();

        if (style == DialogueBubbleStyle.Classic)
        {
            return;
        }

        Vector2 min =
            ImGui.GetWindowPos();

        Vector2 max =
            min + ImGui.GetWindowSize();

        // Tekst nadal mieści się w granicach okna, ale graficzna
        // ramka może wystawać poza nie. Bez tego ImGui ucinał
        // postrzępione końcówki Reliktu oraz narożniki HUD-a.
        Vector2 frameOverscan =
            style == DialogueBubbleStyle.Relic
                ? new Vector2(22.0f, 15.0f)
                : new Vector2(12.0f, 9.0f);

        Vector2 frameMin =
            min - frameOverscan;

        Vector2 frameMax =
            max + frameOverscan;

        var drawList =
            ImGui.GetWindowDrawList();

        // Tymczasowo wyłączamy clip prostokąta okna tylko dla ramki.
        // Dzięki temu transparentne / ozdobne fragmenty PNG mogą
        // normalnie wyjść kilka pikseli poza obszar tekstu.
        drawList.PushClipRectFullScreen();

        try
        {
            if (style == DialogueBubbleStyle.Relic)
            {
                if (DrawNineSliceFrame(
                        relicFrameTexture,
                        frameMin,
                        frameMax,
                        115.0f,
                        76.0f,
                        64.0f,
                        40.0f))
                {
                    DrawRelicDetails(
                        min,
                        max);

                    return;
                }

                DrawRelicFallback(
                    frameMin,
                    frameMax);

                return;
            }

            DrawHudVisibilityBase(
                frameMin,
                frameMax);

            if (DrawNineSliceFrame(
                    hudFrameTexture,
                    frameMin,
                    frameMax,
                    110.0f,
                    70.0f,
                    54.0f,
                    34.0f))
            {
                DrawHudDetails(
                    min,
                    max);

                return;
            }

            DrawHudFallback(
                frameMin,
                frameMax);
        }
        finally
        {
            drawList.PopClipRect();
        }
    }

    private bool DrawNineSliceFrame(
        ISharedImmediateTexture? texture,
        Vector2 min,
        Vector2 max,
        float sourceBorderX,
        float sourceBorderY,
        float destinationBorderX,
        float destinationBorderY)
    {
        var wrap =
            texture?.GetWrapOrDefault();

        if (wrap is null)
        {
            return false;
        }

        Vector2 textureSize =
            wrap.Size;

        float sourceWidth =
            textureSize.X;

        float sourceHeight =
            textureSize.Y;

        if (sourceWidth <= sourceBorderX * 2.0f
            || sourceHeight <= sourceBorderY * 2.0f)
        {
            return false;
        }

        float destinationWidth =
            max.X - min.X;

        float destinationHeight =
            max.Y - min.Y;

        destinationBorderX =
            MathF.Min(
                destinationBorderX,
                destinationWidth * 0.28f);

        destinationBorderY =
            MathF.Min(
                destinationBorderY,
                destinationHeight * 0.42f);

        float[] dx =
        [
            min.X,
            min.X + destinationBorderX,
            max.X - destinationBorderX,
            max.X
        ];

        float[] dy =
        [
            min.Y,
            min.Y + destinationBorderY,
            max.Y - destinationBorderY,
            max.Y
        ];

        float[] ux =
        [
            0.0f,
            sourceBorderX / sourceWidth,
            1.0f - sourceBorderX / sourceWidth,
            1.0f
        ];

        float[] uy =
        [
            0.0f,
            sourceBorderY / sourceHeight,
            1.0f - sourceBorderY / sourceHeight,
            1.0f
        ];

        uint tint =
            ImGui.GetColorU32(
                new Vector4(
                    1.0f,
                    1.0f,
                    1.0f,
                    fadeAlpha));

        var drawList =
            ImGui.GetWindowDrawList();

        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                drawList.AddImage(
                    wrap.Handle,
                    new Vector2(
                        dx[x],
                        dy[y]),
                    new Vector2(
                        dx[x + 1],
                        dy[y + 1]),
                    new Vector2(
                        ux[x],
                        uy[y]),
                    new Vector2(
                        ux[x + 1],
                        uy[y + 1]),
                    tint);
            }
        }

        return true;
    }

    private void DrawRelicDetails(
        Vector2 min,
        Vector2 max)
    {
        var drawList =
            ImGui.GetWindowDrawList();

        uint divider =
            ImGui.GetColorU32(
                new Vector4(
                    0.42f,
                    0.37f,
                    0.29f,
                    0.50f * fadeAlpha));

        float y =
            min.Y + 54.0f;

        drawList.AddLine(
            new Vector2(
                min.X + 42.0f,
                y),
            new Vector2(
                min.X + 285.0f,
                y),
            divider,
            1.0f);

        Vector2 diamond =
            new(
                min.X + 42.0f,
                y);

        drawList.AddQuadFilled(
            new Vector2(diamond.X, diamond.Y - 3.0f),
            new Vector2(diamond.X + 3.0f, diamond.Y),
            new Vector2(diamond.X, diamond.Y + 3.0f),
            new Vector2(diamond.X - 3.0f, diamond.Y),
            divider);
    }

    private void DrawHudVisibilityBase(
        Vector2 min,
        Vector2 max)
    {
        var drawList =
            ImGui.GetWindowDrawList();

        uint shadow =
            ImGui.GetColorU32(
                new Vector4(
                    0.0f,
                    0.0f,
                    0.0f,
                    0.32f * fadeAlpha));

        uint panel =
            ImGui.GetColorU32(
                new Vector4(
                    0.045f,
                    0.038f,
                    0.043f,
                    0.82f * fadeAlpha));

        uint inner =
            ImGui.GetColorU32(
                new Vector4(
                    0.11f,
                    0.075f,
                    0.085f,
                    0.34f * fadeAlpha));

        FillCutCornerPanel(
            drawList,
            min + new Vector2(3.0f, 5.0f),
            max + new Vector2(3.0f, 5.0f),
            18.0f,
            shadow);

        FillCutCornerPanel(
            drawList,
            min,
            max,
            18.0f,
            panel);

        FillCutCornerPanel(
            drawList,
            min + new Vector2(5.0f, 5.0f),
            max - new Vector2(5.0f, 5.0f),
            14.0f,
            inner);
    }

    private void DrawHudDetails(
        Vector2 min,
        Vector2 max)
    {
        var drawList =
            ImGui.GetWindowDrawList();

        uint accent =
            ImGui.GetColorU32(
                new Vector4(
                    0.90f,
                    0.42f,
                    0.48f,
                    0.96f * fadeAlpha));

        float y =
            min.Y + 53.0f;

        drawList.AddLine(
            new Vector2(
                min.X + 36.0f,
                y),
            new Vector2(
                max.X - 86.0f,
                y),
            accent,
            1.7f);

        drawList.AddCircleFilled(
            new Vector2(
                min.X + 36.0f,
                y),
            2.0f,
            accent,
            12);
    }

    private void DrawRelicFallback(
        Vector2 min,
        Vector2 max)
    {
        var drawList =
            ImGui.GetWindowDrawList();

        uint panel =
            ImGui.GetColorU32(
                new Vector4(
                    0.95f,
                    0.94f,
                    0.88f,
                    0.92f * fadeAlpha));

        uint edge =
            ImGui.GetColorU32(
                new Vector4(
                    1.0f,
                    0.99f,
                    0.95f,
                    0.90f * fadeAlpha));

        drawList.AddRectFilled(
            min + new Vector2(3.0f, 3.0f),
            max - new Vector2(3.0f, 3.0f),
            panel,
            18.0f);

        drawList.AddRect(
            min + new Vector2(5.0f, 5.0f),
            max - new Vector2(5.0f, 5.0f),
            edge,
            16.0f,
            ImDrawFlags.None,
            3.0f);

        DrawRelicDetails(
            min,
            max);
    }

    private void DrawHudFallback(
        Vector2 min,
        Vector2 max)
    {
        var drawList =
            ImGui.GetWindowDrawList();

        uint panel =
            ImGui.GetColorU32(
                new Vector4(
                    0.075f,
                    0.070f,
                    0.075f,
                    0.94f * fadeAlpha));

        uint border =
            ImGui.GetColorU32(
                new Vector4(
                    0.52f,
                    0.24f,
                    0.27f,
                    0.90f * fadeAlpha));

        FillCutCornerPanel(
            drawList,
            min,
            max,
            18.0f,
            panel);

        DrawCutCornerBorder(
            drawList,
            min,
            max,
            18.0f,
            border,
            2.0f);

        DrawHudDetails(
            min,
            max);
    }

    private static void FillCutCornerPanel(
        ImDrawListPtr drawList,
        Vector2 min,
        Vector2 max,
        float cut,
        uint color)
    {
        drawList.AddRectFilled(
            new Vector2(min.X + cut, min.Y),
            new Vector2(max.X - cut, max.Y),
            color);

        drawList.AddRectFilled(
            new Vector2(min.X, min.Y + cut),
            new Vector2(max.X, max.Y - cut),
            color);

        drawList.AddTriangleFilled(
            new Vector2(min.X + cut, min.Y),
            new Vector2(min.X + cut, min.Y + cut),
            new Vector2(min.X, min.Y + cut),
            color);

        drawList.AddTriangleFilled(
            new Vector2(max.X - cut, min.Y),
            new Vector2(max.X, min.Y + cut),
            new Vector2(max.X - cut, min.Y + cut),
            color);

        drawList.AddTriangleFilled(
            new Vector2(min.X, max.Y - cut),
            new Vector2(min.X + cut, max.Y - cut),
            new Vector2(min.X + cut, max.Y),
            color);

        drawList.AddTriangleFilled(
            new Vector2(max.X - cut, max.Y - cut),
            new Vector2(max.X, max.Y - cut),
            new Vector2(max.X - cut, max.Y),
            color);
    }

    private static void DrawCutCornerBorder(
        ImDrawListPtr drawList,
        Vector2 min,
        Vector2 max,
        float cut,
        uint color,
        float thickness)
    {
        Vector2 topLeft =
            new(min.X + cut, min.Y);

        Vector2 topRight =
            new(max.X - cut, min.Y);

        Vector2 rightTop =
            new(max.X, min.Y + cut);

        Vector2 rightBottom =
            new(max.X, max.Y - cut);

        Vector2 bottomRight =
            new(max.X - cut, max.Y);

        Vector2 bottomLeft =
            new(min.X + cut, max.Y);

        Vector2 leftBottom =
            new(min.X, max.Y - cut);

        Vector2 leftTop =
            new(min.X, min.Y + cut);

        drawList.AddLine(topLeft, topRight, color, thickness);
        drawList.AddLine(topRight, rightTop, color, thickness);
        drawList.AddLine(rightTop, rightBottom, color, thickness);
        drawList.AddLine(rightBottom, bottomRight, color, thickness);
        drawList.AddLine(bottomRight, bottomLeft, color, thickness);
        drawList.AddLine(bottomLeft, leftBottom, color, thickness);
        drawList.AddLine(leftBottom, leftTop, color, thickness);
        drawList.AddLine(leftTop, topLeft, color, thickness);
    }

    private static void DrawHudOrnament(
        ImDrawListPtr drawList,
        Vector2 center,
        uint color)
    {
        drawList.AddLine(
            new Vector2(center.X, center.Y - 24.0f),
            new Vector2(center.X + 10.0f, center.Y),
            color,
            1.5f);

        drawList.AddLine(
            new Vector2(center.X + 10.0f, center.Y),
            new Vector2(center.X, center.Y + 24.0f),
            color,
            1.5f);

        drawList.AddLine(
            new Vector2(center.X, center.Y + 24.0f),
            new Vector2(center.X - 10.0f, center.Y),
            color,
            1.5f);

        drawList.AddLine(
            new Vector2(center.X - 10.0f, center.Y),
            new Vector2(center.X, center.Y - 24.0f),
            color,
            1.5f);

        drawList.AddLine(
            new Vector2(center.X, center.Y - 12.0f),
            new Vector2(center.X + 5.0f, center.Y),
            color,
            1.0f);

        drawList.AddLine(
            new Vector2(center.X + 5.0f, center.Y),
            new Vector2(center.X, center.Y + 12.0f),
            color,
            1.0f);

        drawList.AddLine(
            new Vector2(center.X, center.Y + 12.0f),
            new Vector2(center.X - 5.0f, center.Y),
            color,
            1.0f);

        drawList.AddLine(
            new Vector2(center.X - 5.0f, center.Y),
            new Vector2(center.X, center.Y - 12.0f),
            color,
            1.0f);
    }

    private static float GetActiveWindowWidth(
        DialogueBubbleStyle style)
    {
        return style switch
        {
            DialogueBubbleStyle.Relic =>
                RelicWindowWidth,

            DialogueBubbleStyle.Hud =>
                HudWindowWidth,

            _ =>
                WindowWidth
        };
    }

    private bool ShouldCoverOriginalDialogue()
    {
        return GetCurrentDisplayKind()
                   == DialogueDisplayKind.Normal
               && plugin.Configuration.NormalDialogueBubbleStyle
                   == DialogueBubbleStyle.Relic
               && plugin.Configuration.CoverOriginalNormalDialogue;
    }

    private DialogueBubbleStyle GetActiveBubbleStyle()
    {
        return plugin.Configuration.NormalDialogueBubbleStyle;
    }

    private DialogueDisplayKind GetCurrentDisplayKind()
    {
        return previewActive
            ? previewDisplayKind
            : plugin.CurrentDialogueDisplayKind;
    }

    private bool IsCinematicDialogue()
    {
        return GetCurrentDisplayKind()
            == DialogueDisplayKind.Cinematic;
    }

    private bool IsBattleTalkDialogue()
    {
        return !previewActive
               && string.Equals(
                   plugin.CurrentDialogueSource,
                   "_BattleTalk",
                   StringComparison.Ordinal);
    }

    private float CalculateCinematicWindowHeight()
    {
        float fontScale =
            GetCinematicFontScale();

        using (cinematicFont.Push())
        {
            List<string> lines =
                WrapCinematicText(
                    currentDialogue.Translation,
                    CinematicTextWidth / fontScale);

            float lineHeight =
                ImGui.GetTextLineHeight()
                * fontScale;

            float textHeight =
                lines.Count <= 0
                    ? lineHeight
                    : lines.Count * lineHeight
                      + MathF.Max(
                          0.0f,
                          lines.Count - 1)
                      * CinematicLineSpacing
                      * fontScale;

            return MathF.Max(
                42.0f,
                textHeight + 12.0f);
        }
    }

    private void DrawCinematicTranslation()
    {
        using (cinematicFont.Push())
        {
            List<string> lines =
                WrapCinematicText(
                    currentDialogue.Translation,
                    CinematicTextWidth);

            float lineHeight =
                ImGui.GetTextLineHeight();

            float totalHeight =
                lines.Count <= 0
                    ? lineHeight
                    : lines.Count * lineHeight
                      + MathF.Max(
                          0.0f,
                          lines.Count - 1)
                      * CinematicLineSpacing;

            float cursorY =
                MathF.Max(
                    0.0f,
                    (ImGui.GetWindowSize().Y - totalHeight)
                    * 0.5f);

            foreach (string line in lines)
            {
                Vector2 textSize =
                    ImGui.CalcTextSize(line);

                float cursorX =
                    MathF.Max(
                        0.0f,
                        (ImGui.GetWindowSize().X - textSize.X)
                        * 0.5f);

                DrawCinematicTextLine(
                    line,
                    cursorX,
                    cursorY);

                cursorY +=
                    lineHeight
                    + CinematicLineSpacing;
            }
        }
    }

    private void DrawCinematicTextLine(
        string text,
        float x,
        float y)
    {
        Vector4 shadowColor =
            new(
                0.0f,
                0.0f,
                0.0f,
                0.88f * fadeAlpha);

        Vector2[] shadowOffsets =
        [
            new Vector2(-2.0f, 0.0f),
            new Vector2(2.0f, 0.0f),
            new Vector2(0.0f, -2.0f),
            new Vector2(0.0f, 2.0f),
            new Vector2(-1.5f, -1.5f),
            new Vector2(1.5f, -1.5f),
            new Vector2(-1.5f, 1.5f),
            new Vector2(1.5f, 1.5f)
        ];

        foreach (Vector2 offset in shadowOffsets)
        {
            ImGui.SetCursorPos(
                new Vector2(
                    x + offset.X,
                    y + offset.Y));

            ImGui.PushStyleColor(
                ImGuiCol.Text,
                shadowColor);

            ImGui.TextUnformatted(
                text);

            ImGui.PopStyleColor();
        }

        ImGui.SetCursorPos(
            new Vector2(
                x,
                y));

        ImGui.PushStyleColor(
            ImGuiCol.Text,
            new Vector4(
                1.0f,
                0.96f,
                0.86f,
                fadeAlpha));

        ImGui.TextUnformatted(
            text);

        ImGui.PopStyleColor();
    }

    private static List<string> WrapCinematicText(
        string text,
        float maxWidth)
    {
        List<string> result =
            new();

        string normalized =
            (text ?? string.Empty)
            .Replace(
                "\r",
                string.Empty);

        string[] paragraphs =
            normalized.Split('\n');

        foreach (string paragraph in paragraphs)
        {
            if (string.IsNullOrWhiteSpace(paragraph))
            {
                result.Add(string.Empty);
                continue;
            }

            string[] words =
                paragraph.Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries);

            string currentLine =
                string.Empty;

            foreach (string word in words)
            {
                string candidate =
                    string.IsNullOrEmpty(currentLine)
                        ? word
                        : $"{currentLine} {word}";

                if (string.IsNullOrEmpty(currentLine)
                    || ImGui.CalcTextSize(candidate).X <= maxWidth)
                {
                    currentLine =
                        candidate;

                    continue;
                }

                result.Add(
                    currentLine);

                currentLine =
                    word;
            }

            if (!string.IsNullOrEmpty(currentLine))
            {
                result.Add(
                    currentLine);
            }
        }

        if (result.Count == 0)
        {
            result.Add(
                string.Empty);
        }

        return result;
    }

    private bool ShouldDrawContent()
    {
        // Jeżeli gra już pokazuje _BattleTalk, ale nasz poller nie zdążył jeszcze
        // przełączyć CurrentDialogueSource, nie wolno dorysować starego dolnego
        // dialogu. To jest druga blokada obok ukrywania całego okna w layoutcie.
        if (!IsBattleTalkDialogue()
            && plugin.IsBattleTalkSurfaceVisibleNow())
        {
            return false;
        }

        // BattleTalk nie może korzystać z dolnego fallbacku. Jeżeli kotwica
        // nie jest jeszcze gotowa, pokazujemy go dopiero po jej uzyskaniu.
        if (IsBattleTalkDialogue()
            && !plugin.TryGetBattleTalkScreenAnchor(
                out _,
                out _))
        {
            return false;
        }

        return fadeAlpha > InvisibleAlphaThreshold
               && !string.IsNullOrWhiteSpace(currentDialogue.Translation);
    }

    private void DrawNpcName()
    {
        DialogueBubbleStyle style =
            GetActiveBubbleStyle();

        IFontHandle font =
            style == DialogueBubbleStyle.Classic
                ? npcNameFont
                : styledNpcNameFont;

        bool pushedColor =
            style == DialogueBubbleStyle.Hud;

        if (pushedColor)
        {
            ImGui.PushStyleColor(
                ImGuiCol.Text,
                new Vector4(
                    1.0f,
                    0.74f,
                    0.77f,
                    fadeAlpha));
        }

        using (font.Push())
        {
            ImGui.TextUnformatted(
                currentDialogue.NpcName);
        }

        if (pushedColor)
        {
            ImGui.PopStyleColor();
        }
    }

    private void DrawTranslation()
    {
        DialogueBubbleStyle style =
            GetActiveBubbleStyle();

        IFontHandle font =
            style == DialogueBubbleStyle.Classic
                ? dialogueFont
                : styledDialogueFont;

        using (font.Push())
        {
            ImGui.TextWrapped(
                currentDialogue.Translation);
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
