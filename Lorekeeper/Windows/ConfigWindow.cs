using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;

namespace Lorekeeper.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private const int ApiKeyInputCapacity = 512;
    private const int ProposalEditInputCapacity = 256;
    private const float UiFontSize = 17.0f;
    private const float SettingsRightPadding = 58.0f;
    private const float SettingsMinimumControlWidth = 500.0f;
    private const float SettingsGroupHeight = 23.0f;
    private const float SettingsFrameRounding = 4.0f;

    private static readonly string[] OpenAiModelIds =
    [
        "gpt-4o-mini",
        "gpt-5.6-luna",
        "gpt-4.1-mini"
    ];

    private static readonly string[] OpenAiModelLabels =
    [
        "GPT-4o mini",
        "GPT-5.6 Luna",
        "GPT-4.1 mini"
    ];

    private static readonly Vector2 InitialWindowSize =
        new(770.0f, 560.0f);

    private static readonly Vector2 MinimumWindowSize =
        new(770.0f, 500.0f);

    private static readonly Vector2 SidebarSize =
        new(170.0f, 0.0f);

    private readonly Configuration configuration;
    private readonly IFontHandle uiFont;
    private readonly ISharedImmediateTexture? logoTexture;
    private readonly TerminologyProposalStore? proposalStore;
    private readonly TerminologyStore? terminologyStore;
    private readonly LibreTranslateRuntimeManager? libreTranslateRuntimeManager;

    private readonly Dictionary<string, string> proposalEdits =
        new(StringComparer.OrdinalIgnoreCase);

    private string apiKey;
    private string model;
    private string statusMessage = string.Empty;
    private bool translationSettingsExpanded;
    private bool windowSettingsExpanded;

    private ConfigTab selectedTab =
        ConfigTab.Terminology;

    private enum ConfigTab
    {
        Terminology,
        Settings,
        Author
    }

    public ConfigWindow(Plugin plugin)
        : this(
            plugin,
            null,
            null,
            null)
    {
    }

    public ConfigWindow(
        Plugin plugin,
        TerminologyProposalStore? proposalStore,
        TerminologyStore? terminologyStore,
        LibreTranslateRuntimeManager? libreTranslateRuntimeManager = null)
        : base($"Lorekeeper {GetPluginVersion()}###LorekeeperConfig")
    {
        configuration = plugin.Configuration;

        string pluginDirectory =
            Plugin.PluginInterface.AssemblyLocation.Directory?.FullName
            ?? Plugin.PluginInterface.ConfigDirectory.FullName;

        string fontPath = Path.Combine(
            pluginDirectory,
            "Assets",
            "Fonts",
            "NotoSans-Medium.ttf");

        uiFont = CreateFontHandle(
            fontPath,
            UiFontSize);

        string logoPath = Path.Combine(
            pluginDirectory,
            "Assets",
            "Arts",
            "Logo.png");

        logoTexture = File.Exists(logoPath)
            ? Plugin.TextureProvider.GetFromFile(logoPath)
            : null;

        this.proposalStore = proposalStore;
        this.terminologyStore = terminologyStore;
        this.libreTranslateRuntimeManager = libreTranslateRuntimeManager;

        apiKey = configuration.OpenAiApiKey;
        model = configuration.OpenAiModel;

        Size = InitialWindowSize;
        SizeCondition = ImGuiCond.FirstUseEver;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = MinimumWindowSize,
            MaximumSize = new Vector2(
                4096.0f,
                4096.0f)
        };
    }

    public void Dispose()
    {
        uiFont.Dispose();
    }

    public override void PreDraw()
    {
        SetWindowMovability(
            configuration.IsConfigWindowMovable);
    }

    public override void Draw()
    {
        using (uiFont.Push())
        {
            DrawMainLayout();
        }
    }

    private void DrawMainLayout()
    {
        ImGui.BeginChild(
            "##LorekeeperSidebar",
            SidebarSize,
            true);

        DrawSidebar();

        ImGui.EndChild();

        ImGui.SameLine();

        ImGui.BeginChild(
            "##LorekeeperContent",
            new Vector2(0.0f, 0.0f),
            true);

        DrawContent();

        ImGui.EndChild();
    }

    private void DrawSidebar()
    {
        DrawBrand();

        ImGui.Spacing();
        ImGui.Spacing();

        DrawSidebarTab(
            ConfigTab.Terminology,
            "Terminy");

        ImGui.Dummy(
            new Vector2(
                0.0f,
                14.0f));

        DrawSidebarTab(
            ConfigTab.Settings,
            "Ustawienia");

        DrawSidebarTab(
            ConfigTab.Author,
            "Autor");
    }

    private void DrawBrand()
    {
        float availableWidth =
            ImGui.GetContentRegionAvail().X;

        float logoSizeValue =
            MathF.Max(
                64.0f,
                availableWidth - 8.0f);

        float brandHeight =
            logoSizeValue + 8.0f;

        ImGui.BeginChild(
            "##LorekeeperBrand",
            new Vector2(
                0.0f,
                brandHeight),
            false);

        availableWidth =
            ImGui.GetContentRegionAvail().X;

        logoSizeValue =
            MathF.Max(
                64.0f,
                availableWidth - 4.0f);

        Vector2 logoSize =
            new(
                logoSizeValue,
                logoSizeValue);

        float logoX =
            MathF.Max(
                0.0f,
                (availableWidth - logoSize.X) * 0.5f);

        ImGui.SetCursorPosX(
            ImGui.GetCursorPosX() + logoX);

        var logoWrap =
            logoTexture?.GetWrapOrDefault();

        if (logoWrap is not null)
        {
            ImGui.Image(
                logoWrap.Handle,
                logoSize);
        }
        else
        {
            ImGui.Button(
                "LK",
                logoSize);
        }

        ImGui.EndChild();
    }

    private void DrawSidebarTab(
        ConfigTab tab,
        string label)
    {
        bool isSelected =
            selectedTab == tab;

        ImGui.PushStyleVar(
            ImGuiStyleVar.FramePadding,
            new Vector2(
                6.0f,
                4.0f));

        if (ImGui.Selectable(
                label,
                isSelected))
        {
            selectedTab = tab;
            statusMessage =
                string.Empty;
        }

        ImGui.PopStyleVar();
    }

    private void DrawContent()
    {
        DrawContentHeader();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        switch (selectedTab)
        {
            case ConfigTab.Terminology:
                DrawTerminologyTab();
                break;

            case ConfigTab.Settings:
                DrawSettingsTab();
                break;

            case ConfigTab.Author:
                DrawAuthorTab();
                break;
        }

        if (string.IsNullOrWhiteSpace(
                statusMessage))
        {
            return;
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextWrapped(
            statusMessage);
    }

    private void DrawContentHeader()
    {
        string title =
            selectedTab switch
            {
                ConfigTab.Terminology =>
                    "Terminologia",

                ConfigTab.Settings =>
                    "Ustawienia",

                ConfigTab.Author =>
                    "Autor",

                _ =>
                    "Lorekeeper"
            };

        string description =
            selectedTab switch
            {
                ConfigTab.Terminology =>
                    "Tutaj przeglądasz i zatwierdzasz proponowane terminy.",

                ConfigTab.Settings =>
                    "Ta zakładka służy do customizacji globalnych ustawień Lorekeepera.",

                ConfigTab.Author =>
                    "Informacje o pluginie.",

                _ =>
                    string.Empty
            };

        ImGui.TextUnformatted(
            title);

        ImGui.TextWrapped(
            description);
    }

    private void DrawSettingsTab()
    {
        if (DrawSettingsGroupHeader(
                "Silnik tłumaczeń",
                ref translationSettingsExpanded))
        {
            ImGui.Spacing();

            DrawTranslationProviderTab();

            ImGui.Spacing();
            DrawSettingsSeparator();
            ImGui.Spacing();

            if (configuration.SelectedTranslationProvider
                == TranslationProvider.LibreTranslate)
            {
                DrawLibreTranslateTab();
            }
            else
            {
                DrawOpenAiTab();
            }

            ImGui.Spacing();
        }

        ImGui.Spacing();

        if (DrawSettingsGroupHeader(
                "Okno",
                ref windowSettingsExpanded))
        {
            ImGui.Spacing();

            DrawWindowTab();

            ImGui.Spacing();
        }
    }

    private bool DrawSettingsGroupHeader(
        string label,
        ref bool expanded)
    {
        float width =
            GetSettingsControlWidth();

        Vector2 headerPosition =
            ImGui.GetCursorScreenPos();

        ImGui.PushStyleVar(
            ImGuiStyleVar.FrameRounding,
            SettingsFrameRounding);
        ImGui.PushStyleVar(
            ImGuiStyleVar.ButtonTextAlign,
            new Vector2(
                0.0f,
                0.5f));

        bool clicked = ImGui.Button(
            $"      {label}##SettingsGroup{label}",
            new Vector2(
                width,
                SettingsGroupHeight));

        ImGui.PopStyleVar(2);

        DrawSettingsGroupArrow(
            headerPosition,
            expanded);

        if (clicked)
        {
            expanded =
                !expanded;
        }

        return expanded;
    }

    private static void DrawSettingsGroupArrow(
        Vector2 headerPosition,
        bool expanded)
    {
        float centerX =
            headerPosition.X + 13.0f;

        float centerY =
            headerPosition.Y
            + (SettingsGroupHeight * 0.5f);

        uint color =
            ImGui.GetColorU32(
                ImGuiCol.Text);

        if (expanded)
        {
            ImGui.GetWindowDrawList().AddTriangleFilled(
                new Vector2(
                    centerX - 4.0f,
                    centerY - 2.5f),
                new Vector2(
                    centerX + 4.0f,
                    centerY - 2.5f),
                new Vector2(
                    centerX,
                    centerY + 4.0f),
                color);

            return;
        }

        ImGui.GetWindowDrawList().AddTriangleFilled(
            new Vector2(
                centerX - 2.5f,
                centerY - 4.0f),
            new Vector2(
                centerX - 2.5f,
                centerY + 4.0f),
            new Vector2(
                centerX + 4.0f,
                centerY),
            color);
    }

    private float GetSettingsControlWidth()
    {
        float availableWidth =
            ImGui.GetContentRegionAvail().X;

        float width =
            MathF.Max(
                SettingsMinimumControlWidth,
                availableWidth - SettingsRightPadding);

        return MathF.Min(
            width,
            availableWidth);
    }

    private void DrawSettingsSeparator()
    {
        float startX =
            ImGui.GetCursorScreenPos().X;

        float y =
            ImGui.GetCursorScreenPos().Y;

        float width =
            GetSettingsControlWidth();

        uint color =
            ImGui.GetColorU32(
                ImGuiCol.Separator);

        ImGui.GetWindowDrawList().AddLine(
            new Vector2(
                startX,
                y),
            new Vector2(
                startX + width,
                y),
            color);

        ImGui.Dummy(
            new Vector2(
                width,
                1.0f));
    }

    private void DrawTranslationProviderTab()
    {
        ImGui.TextDisabled(
            "Tłumaczenia OpenAI - lokalne i z Lorekeeper Cloud - mają zawsze pierwszeństwo.");

        ImGui.Spacing();

        bool openAiSelected =
            configuration.SelectedTranslationProvider
            == TranslationProvider.OpenAI;

        if (ImGui.RadioButton(
                "OpenAI",
                openAiSelected))
        {
            SelectTranslationProvider(
                TranslationProvider.OpenAI);
        }

        ImGui.SameLine();

        bool libreSelected =
            configuration.SelectedTranslationProvider
            == TranslationProvider.LibreTranslate;

        if (ImGui.RadioButton(
                "LibreTranslate",
                libreSelected))
        {
            SelectTranslationProvider(
                TranslationProvider.LibreTranslate);
        }
    }

    private void DrawLibreTranslateTab()
    {
        ImGui.TextUnformatted(
            "LibreTranslate");

        ImGui.Spacing();

        ImGui.TextWrapped(
            "Darmowy lokalny translator uruchamiany bezpośrednio przez Lorekeepera.");

        ImGui.Spacing();

        if (libreTranslateRuntimeManager is null)
        {
            ImGui.TextDisabled(
                "Manager LibreTranslate nie jest dostępny w tej kompilacji.");

            return;
        }

        DrawLibreRuntimeStatus(
            libreTranslateRuntimeManager);

        if (libreTranslateRuntimeManager.IsBusy
            && libreTranslateRuntimeManager.Status
                != LibreTranslateRuntimeStatus.Removing)
        {
            DrawLibreInstallationProgress(
                libreTranslateRuntimeManager);
        }

        ImGui.Spacing();

        if (libreTranslateRuntimeManager.Status
            == LibreTranslateRuntimeStatus.Error)
        {
            ImGui.TextWrapped(
                $"Błąd: {libreTranslateRuntimeManager.LastError}");

            ImGui.Spacing();
        }

        if (libreTranslateRuntimeManager.IsBusy)
        {
            ImGui.TextDisabled(
                "Instalacja/uruchamianie trwa w tle. Nie zamykaj gry podczas instalacji.");

            return;
        }

        ImGui.PushStyleVar(
            ImGuiStyleVar.FrameRounding,
            SettingsFrameRounding);

        if (libreTranslateRuntimeManager.IsReady)
        {
            if (ImGui.Button(
                    "Przeinstaluj"))
            {
                _ = libreTranslateRuntimeManager.InstallAsync(
                    reinstall: true);
            }

            ImGui.SameLine();

            if (ImGui.Button(
                    "Usuń"))
            {
                _ = libreTranslateRuntimeManager.RemoveAsync();
            }
        }
        else
        {
            string installLabel =
                libreTranslateRuntimeManager.IsInstalled
                    ? "Uruchom ponownie"
                    : "Zainstaluj";

            if (ImGui.Button(
                    installLabel))
            {
                if (libreTranslateRuntimeManager.IsInstalled)
                {
                    _ = libreTranslateRuntimeManager.StartIfInstalledAsync();
                }
                else
                {
                    _ = libreTranslateRuntimeManager.InstallAsync();
                }
            }
        }

        ImGui.PopStyleVar();

        if (libreTranslateRuntimeManager.Status
            == LibreTranslateRuntimeStatus.NotInstalled)
        {
            ImGui.Spacing();

            ImGui.TextDisabled(
                "Lorekeeper zainstaluje wszystko lokalnie. " +
                "Nie musisz instalować Pythona ani Dockera.");
        }
    }

    private void DrawLibreInstallationProgress(
        LibreTranslateRuntimeManager runtimeManager)
    {
        int progress =
            Math.Clamp(
                runtimeManager.InstallationProgressPercent,
                0,
                100);

        ImGui.Spacing();

        ImGui.PushStyleVar(
            ImGuiStyleVar.FrameRounding,
            SettingsFrameRounding);

        ImGui.ProgressBar(
            progress / 100.0f,
            new Vector2(
                GetSettingsControlWidth(),
                18.0f),
            $"{progress}%");

        ImGui.PopStyleVar();

        ImGui.Spacing();
    }

    private static void DrawLibreRuntimeStatus(
        LibreTranslateRuntimeManager runtimeManager)
    {
        ImGui.TextUnformatted(
            "Status:");

        ImGui.SameLine();

        Vector2 iconPosition =
            ImGui.GetCursorScreenPos();

        float lineHeight =
            ImGui.GetTextLineHeight();

        Vector2 center = new(
            MathF.Floor(iconPosition.X) + 5.0f,
            MathF.Floor(iconPosition.Y + (lineHeight * 0.5f)));

        Vector4 dotColor =
            runtimeManager.Status switch
            {
                LibreTranslateRuntimeStatus.Ready =>
                    new Vector4(0.35f, 0.85f, 0.45f, 1.0f),

                LibreTranslateRuntimeStatus.Error =>
                    new Vector4(0.95f, 0.35f, 0.35f, 1.0f),

                _ =>
                    new Vector4(0.55f, 0.55f, 0.55f, 1.0f)
            };

        ImGui.GetWindowDrawList().AddCircleFilled(
            center,
            4.0f,
            ImGui.GetColorU32(dotColor),
            16);

        ImGui.Dummy(
            new Vector2(
                11.0f,
                lineHeight));

        ImGui.SameLine();

        ImGui.TextUnformatted(
            runtimeManager.StatusText);
    }

    private void DrawAuthorTab()
    {
        ImGui.TextUnformatted(
            "Lorekeeper");

        ImGui.Spacing();

        ImGui.TextWrapped(
            "Plugin do tłumaczenia dialogów NPC w Final Fantasy XIV.");

        ImGui.Spacing();

        ImGui.TextDisabled(
            $"Wersja: {GetPluginVersion()}");
    }

    private void DrawOpenAiTab()
    {
        ImGui.TextWrapped(
            "Klucz OpenAI API zostanie zapisany lokalnie " +
            "w konfiguracji pluginu.");

        ImGui.TextDisabled(
            "Nowe tłumaczenia OpenAI są synchronizowane ze wspólną biblioteką Lorekeeper Cloud. " +
            "LibreTranslate pozostaje wyłącznie lokalny.");

        ImGui.Spacing();

        ImGui.Text(
            "OpenAI API Key");

        ImGui.SameLine();
        DrawInfoTooltip(
            "Klucz utworzysz na platform.openai.com w sekcji API Keys. " +
            "Wybierz Create new secret key, skopiuj go po utworzeniu " +
            "i wklej tutaj. Pełny klucz jest wyświetlany tylko podczas tworzenia.");

        ImGui.SetNextItemWidth(
            GetSettingsControlWidth());

        ImGui.InputText(
            "##OpenAiApiKey",
            ref apiKey,
            ApiKeyInputCapacity,
            ImGuiInputTextFlags.Password);

        ImGui.Spacing();

        ImGui.Text(
            "Model");

        DrawOpenAiModelCombo();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.PushStyleVar(
            ImGuiStyleVar.FrameRounding,
            SettingsFrameRounding);

        if (ImGui.Button(
                "Zapisz ustawienia"))
        {
            SaveSettings();
        }

        ImGui.PopStyleVar();
    }

    private void DrawOpenAiModelCombo()
    {
        ImGui.SetNextItemWidth(
            GetSettingsControlWidth());

        string preview = GetOpenAiModelLabel(model);

        if (!ImGui.BeginCombo(
                "##OpenAiModel",
                preview))
        {
            return;
        }

        for (int i = 0; i < OpenAiModelIds.Length; i++)
        {
            string modelId = OpenAiModelIds[i];
            string label = OpenAiModelLabels[i];
            bool selected = string.Equals(
                model,
                modelId,
                StringComparison.OrdinalIgnoreCase);

            if (ImGui.Selectable(
                    label,
                    selected))
            {
                model = modelId;
            }

            DrawModelInfoIconForLastItem(
                GetOpenAiModelInfo(modelId));

            if (selected)
            {
                ImGui.SetItemDefaultFocus();
            }
        }

        ImGui.EndCombo();
    }

    private static string GetOpenAiModelLabel(
        string modelId)
    {
        for (int i = 0; i < OpenAiModelIds.Length; i++)
        {
            if (string.Equals(
                    modelId,
                    OpenAiModelIds[i],
                    StringComparison.OrdinalIgnoreCase))
            {
                return OpenAiModelLabels[i];
            }
        }

        return OpenAiModelLabels[0];
    }

    private static string GetOpenAiModelInfo(
        string modelId)
    {
        if (string.Equals(
                modelId,
                "gpt-5.6-luna",
                StringComparison.OrdinalIgnoreCase))
        {
            return "GPT-5.6 Luna - model zoptymalizowany pod tanią pracę przy dużej liczbie zapytań. " +
                   "Cena za 1 mln tokenów: input $0.20, cached input $0.02, output $1.20.";
        }

        if (string.Equals(
                modelId,
                "gpt-4.1-mini",
                StringComparison.OrdinalIgnoreCase))
        {
            return "GPT-4.1 mini - szybki model dobrze trzymający instrukcje i format odpowiedzi. " +
                   "Cena za 1 mln tokenów: input $0.40, cached input $0.10, output $1.60.";
        }

        return "GPT-4o mini - ekonomiczny i szybki model, dobry do krótkich tłumaczeń. " +
               "Cena za 1 mln tokenów: input $0.15, cached input $0.075, output $0.60.";
    }

    private static void DrawModelInfoIconForLastItem(
        string text)
    {
        const float iconRadius = 6.0f;

        Vector2 itemMin = ImGui.GetItemRectMin();
        Vector2 itemMax = ImGui.GetItemRectMax();
        Vector2 center = new(
            MathF.Floor(itemMax.X - 13.0f),
            MathF.Floor((itemMin.Y + itemMax.Y) * 0.5f));

        Vector2 hitMin = new(
            center.X - 8.0f,
            center.Y - 8.0f);
        Vector2 hitMax = new(
            center.X + 8.0f,
            center.Y + 8.0f);

        bool hovered = ImGui.IsMouseHoveringRect(
            hitMin,
            hitMax);

        uint circleColor = ImGui.GetColorU32(
            hovered
                ? ImGuiCol.ButtonHovered
                : ImGuiCol.TextDisabled);
        uint infoColor = ImGui.GetColorU32(
            ImGuiCol.WindowBg);
        var drawList = ImGui.GetWindowDrawList();

        drawList.AddCircleFilled(
            center,
            iconRadius,
            circleColor,
            24);

        drawList.AddCircleFilled(
            new Vector2(center.X, center.Y - 2.6f),
            0.95f,
            infoColor,
            12);

        drawList.AddRectFilled(
            new Vector2(center.X - 0.75f, center.Y - 0.1f),
            new Vector2(center.X + 0.75f, center.Y + 3.4f),
            infoColor,
            0.75f);

        if (!hovered)
        {
            return;
        }

        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(
            ImGui.GetFontSize() * 28.0f);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

    private static void DrawInfoTooltip(
        string text)
    {
        const float iconSize = 16.0f;
        const float iconRadius = 6.5f;

        Vector2 iconPosition = ImGui.GetCursorScreenPos();

        // Hitbox jest niewidzialny. Cała ikona jest rysowana z prymitywów,
        // więc nie zależy od fontu ani od FontAwesome.
        ImGui.PushID(text);
        ImGui.InvisibleButton(
            "##InfoDot",
            new Vector2(iconSize, iconSize));

        bool hovered = ImGui.IsItemHovered();

        // Snap do pełnych pikseli usuwa wrażenie przekrzywienia małej ikony.
        Vector2 center = new(
            MathF.Floor(iconPosition.X) + 8.0f,
            MathF.Floor(iconPosition.Y) + 8.0f);

        uint circleColor = ImGui.GetColorU32(
            hovered
                ? ImGuiCol.ButtonHovered
                : ImGuiCol.TextDisabled);

        uint infoColor = ImGui.GetColorU32(ImGuiCol.WindowBg);
        var drawList = ImGui.GetWindowDrawList();

        drawList.AddCircleFilled(
            center,
            iconRadius,
            circleColor,
            24);

        // Symetryczne "i": osobna okrągła kropka + prostokątny trzon.
        // Nie używamy AddLine, bo antyaliasing cienkiej linii potrafił wyglądać krzywo.
        drawList.AddCircleFilled(
            new Vector2(center.X, center.Y - 2.8f),
            1.05f,
            infoColor,
            12);

        drawList.AddRectFilled(
            new Vector2(center.X - 0.8f, center.Y - 0.2f),
            new Vector2(center.X + 0.8f, center.Y + 3.8f),
            infoColor,
            0.8f);

        if (hovered)
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 28.0f);
            ImGui.TextUnformatted(text);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }

        ImGui.PopID();
    }

    private void DrawWindowTab()
    {
        bool isMovable =
            configuration
                .IsConfigWindowMovable;

        if (ImGui.Checkbox(
                "Pozw\u00f3l przesuwa\u0107 okno",
                ref isMovable))
        {
            configuration
                .IsConfigWindowMovable =
                    isMovable;

            configuration.Save();

            statusMessage =
                "Zapisano ustawienia okna.";
        }

        ImGui.Spacing();

        ImGui.TextWrapped(
            "Je\u017celi opcja jest wy\u0142\u0105czona, " +
            "okno /lore zostanie przypi\u0119te w miejscu " +
            "i nie b\u0119dzie mo\u017cna przesuwa\u0107 go myszk\u0105.");
    }

    private void DrawTerminologyTab()
    {
        if (proposalStore is null
            || terminologyStore is null)
        {
            ImGui.TextDisabled(
                "Modu\u0142 propozycji nie jest jeszcze pod\u0142\u0105czony.");

            return;
        }

        IReadOnlyList<TerminologyProposal> pending =
            proposalStore.GetPending();

        ImGui.TextWrapped(
            "Tutaj mo\u017cesz akceptowa\u0107, poprawia\u0107 lub " +
            "odrzuca\u0107 propozycje termin\u00f3w wykryte przez plugin.");

        ImGui.Spacing();

        ImGui.TextDisabled(
            $"Oczekuj\u0105ce propozycje: {pending.Count}");

        ImGui.Spacing();

        if (pending.Count == 0)
        {
            ImGui.TextDisabled(
                "Brak nowych propozycji.");

            return;
        }

        foreach (
            TerminologyProposal proposal
            in pending)
        {
            DrawTerminologyProposal(
                proposal);
        }
    }

    private void DrawTerminologyProposal(
        TerminologyProposal proposal)
    {
        ImGui.PushID(
            proposal.SourceTerm);

        ImGui.BeginChild(
            "##ProposalCard",
            new Vector2(
                0.0f,
                142.0f),
            true);

        ImGui.TextUnformatted(
            proposal.SourceTerm);

        ImGui.TextDisabled(
            $"Propozycja: " +
            $"{proposal.ProposedTranslation}");

        ImGui.TextDisabled(
            $"Wyst\u0105pienia: " +
            $"{proposal.Occurrences}  |  " +
            $"Pewno\u015b\u0107: " +
            $"{proposal.Confidence:P0}");

        if (!proposalEdits.TryGetValue(
                proposal.SourceTerm,
                out string? editedTranslation))
        {
            editedTranslation =
                proposal.ProposedTranslation;

            proposalEdits[
                proposal.SourceTerm] =
                    editedTranslation;
        }

        ImGui.Spacing();

        ImGui.Text(
            "Finalne t\u0142umaczenie");

        ImGui.SetNextItemWidth(
            -1.0f);

        string editValue =
            editedTranslation;

        if (ImGui.InputText(
                "##EditedTranslation",
                ref editValue,
                ProposalEditInputCapacity))
        {
            proposalEdits[
                proposal.SourceTerm] =
                    editValue;
        }

        ImGui.Spacing();

        if (ImGui.Button(
                "Akceptuj",
                new Vector2(
                    120.0f,
                    30.0f)))
        {
            proposalStore.Accept(
                proposal.SourceTerm,
                terminologyStore,
                proposalEdits[
                    proposal.SourceTerm]);

            proposalEdits.Remove(
                proposal.SourceTerm);

            statusMessage =
                $"Zaakceptowano termin: " +
                $"{proposal.SourceTerm}";
        }

        ImGui.SameLine();

        if (ImGui.Button(
                "Odrzu\u0107",
                new Vector2(
                    120.0f,
                    30.0f)))
        {
            proposalStore.Reject(
                proposal.SourceTerm);

            proposalEdits.Remove(
                proposal.SourceTerm);

            statusMessage =
                $"Odrzucono termin: " +
                $"{proposal.SourceTerm}";
        }

        ImGui.EndChild();

        ImGui.PopID();

        ImGui.Spacing();
    }

    private void SaveSettings()
    {
        configuration.OpenAiApiKey =
            apiKey.Trim();

        configuration.OpenAiModel =
            model.Trim();

        configuration.Save();

        statusMessage =
            "Zapisano ustawienia OpenAI.";
    }

    private void SelectTranslationProvider(
        TranslationProvider provider)
    {
        if (configuration.SelectedTranslationProvider
            == provider)
        {
            return;
        }

        configuration.SelectedTranslationProvider =
            provider;

        configuration.Save();

        statusMessage = provider switch
        {
            TranslationProvider.LibreTranslate =>
                "Wybrano LibreTranslate. Zmiana działa od następnego dialogu.",
            _ =>
                "Wybrano OpenAI. Zmiana działa od następnego dialogu."
        };
    }

    private void SetWindowMovability(
        bool isMovable)
    {
        if (isMovable)
        {
            Flags &=
                ~ImGuiWindowFlags.NoMove;

            return;
        }

        Flags |=
            ImGuiWindowFlags.NoMove;
    }

    private static string GetPluginVersion()
    {
        Version? version =
            typeof(Plugin).Assembly
                .GetName()
                .Version;

        return version?.ToString()
            ?? "0.0.0.0";
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
                                    $"Załadowano font UI {fontSize}px: {fontPath}");

                                return;
                            }

                            Plugin.Log.Warning(
                                $"Nie znaleziono fontu UI: {fontPath}");

                            toolkit.AddDalamudDefaultFont(
                                fontSize);
                        }));
    }

}
