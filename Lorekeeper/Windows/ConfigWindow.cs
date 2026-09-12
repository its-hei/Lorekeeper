using System;
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
    private const float UiFontSize = 17.0f;
    private const float SettingsRightPadding = 58.0f;
    private const float SettingsMinimumControlWidth = 500.0f;
    private const float SettingsGroupHeight = 23.0f;
    private const float SettingsFrameRounding = 4.0f;
    private const float BubbleStyleCardHeight = 82.0f;

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

    private readonly Plugin plugin;
    private readonly Configuration configuration;
    private readonly IFontHandle uiFont;
    private readonly ISharedImmediateTexture? logoTexture;
    private readonly LibreTranslateRuntimeManager? libreTranslateRuntimeManager;

    private string apiKey;
    private string model;
    private string statusMessage = string.Empty;
    private bool translationSettingsExpanded;
    private bool bubbleSettingsExpanded;
    private bool windowSettingsExpanded;
    private bool requestLibreInstallConfirmation;
    private bool libreInstallIsReinstall;
    private bool requestTranslationCacheResetConfirmation;
    private bool resetInformationSections = true;

    private ConfigTab selectedTab =
        ConfigTab.Information;

    private enum ConfigTab
    {
        Information,
        Settings,
        Author
    }

    public ConfigWindow(Plugin plugin)
        : this(
            plugin,
            null)
    {
    }

    public ConfigWindow(
        Plugin plugin,
        LibreTranslateRuntimeManager? libreTranslateRuntimeManager = null)
        : base($"Lorekeeper {GetPluginVersion()}###LorekeeperConfig")
    {
        this.plugin = plugin;
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
        ImGui.Separator();
        ImGui.Spacing();

        DrawSidebarTab(
            ConfigTab.Information,
            "Informacje");

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

        const float tabHeight =
            27.0f;

        float width =
            ImGui.GetContentRegionAvail().X;

        Vector2 start =
            ImGui.GetCursorScreenPos();

        ImGui.PushID(
            $"Sidebar{tab}");

        bool clicked =
            ImGui.InvisibleButton(
                "##Tab",
                new Vector2(
                    width,
                    tabHeight));

        bool hovered =
            ImGui.IsItemHovered();

        var drawList =
            ImGui.GetWindowDrawList();

        if (isSelected || hovered)
        {
            Vector4 background =
                isSelected
                    ? ImGui.GetStyle().Colors[
                        (int)ImGuiCol.Header]
                    : ImGui.GetStyle().Colors[
                        (int)ImGuiCol.HeaderHovered];

            drawList.AddRectFilled(
                start,
                start + new Vector2(
                    width,
                    tabHeight),
                ImGui.GetColorU32(
                    background));
        }

        Vector2 textSize =
            ImGui.CalcTextSize(
                label);

        Vector2 textPosition =
            start + new Vector2(
                MathF.Max(
                    0.0f,
                    (width - textSize.X) * 0.5f),
                MathF.Max(
                    0.0f,
                    (tabHeight - textSize.Y) * 0.5f));

        drawList.AddText(
            textPosition,
            ImGui.GetColorU32(
                ImGuiCol.Text),
            label);

        if (clicked)
        {
            selectedTab = tab;

            if (tab == ConfigTab.Information)
            {
                resetInformationSections = true;
            }

            requestLibreInstallConfirmation = false;
            libreInstallIsReinstall = false;
            requestTranslationCacheResetConfirmation = false;
            statusMessage =
                string.Empty;
        }

        ImGui.PopID();
    }

    private void DrawContent()
    {
        if (requestLibreInstallConfirmation)
        {
            DrawLibreInstallConfirmationView();
            return;
        }

        switch (selectedTab)
        {
            case ConfigTab.Information:
                DrawInformationTab();
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

    private void DrawSettingsTab()
    {
        bool translationWasExpanded =
            translationSettingsExpanded;

        if (DrawSettingsGroupHeader(
                "Silnik tłumaczeń",
                ref translationSettingsExpanded))
        {
            if (!translationWasExpanded)
            {
                bubbleSettingsExpanded = false;
                windowSettingsExpanded = false;
            }

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
            DrawSettingsSeparator();
            ImGui.Spacing();

            DrawTranslationStorageControls();

            ImGui.Spacing();
        }

        ImGui.Spacing();

        bool bubbleWasExpanded =
            bubbleSettingsExpanded;

        if (DrawSettingsGroupHeader(
                "Dymki",
                ref bubbleSettingsExpanded))
        {
            if (!bubbleWasExpanded)
            {
                translationSettingsExpanded = false;
                windowSettingsExpanded = false;
            }

            ImGui.Spacing();

            DrawBubbleSettingsTab();

            ImGui.Spacing();
        }

        ImGui.Spacing();

        bool uiWasExpanded =
            windowSettingsExpanded;

        if (DrawSettingsGroupHeader(
                "UI",
                ref windowSettingsExpanded))
        {
            if (!uiWasExpanded)
            {
                translationSettingsExpanded = false;
                bubbleSettingsExpanded = false;
            }

            ImGui.Spacing();

            DrawUiTab();

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
            "Tłumaczenia zapisane wcześniej przez OpenAI mają zawsze pierwszeństwo.");

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

        ImGui.Spacing();

        ImGui.TextDisabled(
            "Opcje i menu wyboru pozostają w oryginale.");
    }

    private void DrawTranslationStorageControls()
    {
        float availableWidth =
            ImGui.GetContentRegionAvail().X;

        float rightColumnWidth =
            MathF.Min(
                220.0f,
                availableWidth * 0.32f);

        float columnSpacing =
            150.0f;

        float leftColumnWidth =
            MathF.Max(
                260.0f,
                availableWidth - rightColumnWidth - columnSpacing);

        ImGui.BeginGroup();

        bool cloudEnabled =
            configuration.CloudEnabled;

        if (ImGui.Checkbox(
                "Korzystaj z Lorekeeper Cloud",
                ref cloudEnabled))
        {
            configuration.CloudEnabled =
                cloudEnabled;

            configuration.Save();
        }

        ImGui.Spacing();

        DrawLocalTranslationDatabaseControls();

        ImGui.EndGroup();

        ImGui.SameLine(
            0.0f,
            columnSpacing);

        ImGui.BeginGroup();
        ImGui.SetNextItemWidth(
            rightColumnWidth);

        ImGui.TextUnformatted(
            "Zużycie OpenAI");

        DrawOpenAiUsageLine(
            "Sesja",
            plugin.OpenAiSessionCostUsd);

        DrawOpenAiUsageLine(
            "Łącznie",
            plugin.OpenAiTotalCostUsd);
        ImGui.EndGroup();
    }

    private void DrawLocalTranslationDatabaseControls()
    {
        if (!requestTranslationCacheResetConfirmation)
        {
            ImGui.PushStyleVar(
                ImGuiStyleVar.FrameRounding,
                SettingsFrameRounding);

            if (ImGui.Button(
                    "Wyczyść lokalną bazę tłumaczeń"))
            {
                requestTranslationCacheResetConfirmation =
                    true;
            }

            ImGui.PopStyleVar();
            return;
        }

        ImGui.TextWrapped(
            "Usunąć wszystkie lokalnie zapisane tłumaczenia OpenAI i LibreTranslate?");

        ImGui.Spacing();

        ImGui.PushStyleVar(
            ImGuiStyleVar.FrameRounding,
            SettingsFrameRounding);

        if (ImGui.Button(
                "Anuluj"))
        {
            requestTranslationCacheResetConfirmation =
                false;
        }

        ImGui.SameLine();

        if (ImGui.Button(
                "Wyczyść"))
        {
            if (plugin.TryResetLocalTranslationDatabase(
                    out int removedEntries,
                    out string errorMessage))
            {
                statusMessage =
                    $"Wyczyszczono lokalną bazę tłumaczeń ({removedEntries} wpisów).";
            }
            else
            {
                statusMessage =
                    errorMessage;
            }

            requestTranslationCacheResetConfirmation =
                false;
        }

        ImGui.PopStyleVar();
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
                libreInstallIsReinstall = true;
                requestLibreInstallConfirmation = true;
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
                    libreInstallIsReinstall = false;
                    requestLibreInstallConfirmation = true;
                }
            }
        }

        ImGui.PopStyleVar();

        if (libreTranslateRuntimeManager.Status
            == LibreTranslateRuntimeStatus.NotInstalled)
        {
            ImGui.Spacing();

            ImGui.TextDisabled(
                "Instalacja pobiera dodatkowe komponenty i uruchamia je lokalnie. " +
                "Nie musisz ręcznie instalować Pythona ani Dockera.");
        }

    }

    private void DrawLibreInstallConfirmationView()
    {
        ImGui.TextUnformatted(
            "Instalacja LibreTranslate");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextUnformatted(
            "LibreTranslate wymaga dodatkowych lokalnych komponentów.");

        ImGui.Spacing();

        ImGui.PushTextWrapPos(
            ImGui.GetCursorPosX()
            + MathF.Min(
                500.0f,
                MathF.Max(
                    280.0f,
                    ImGui.GetContentRegionAvail().X - 10.0f)));

        ImGui.TextWrapped(
            "Lorekeeper pobierze przenośny runtime Python, pakiety LibreTranslate " +
            "oraz wymagane dane językowe, a następnie uruchomi lokalny translator " +
            "na Twoim komputerze.");

        ImGui.Spacing();

        ImGui.TextWrapped(
            "LibreTranslate jest zewnętrznym projektem open-source i nie jest " +
            "częścią Dalamuda ani Final Fantasy XIV.");

        ImGui.Spacing();

        ImGui.TextWrapped(
            "Komponenty zostaną zapisane w katalogu Lorekeepera. " +
            "Kontynuując, zgadzasz się na ich pobranie i uruchomienie.");

        ImGui.PopTextWrapPos();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.PushStyleVar(
            ImGuiStyleVar.FrameRounding,
            SettingsFrameRounding);

        if (ImGui.Button(
                "Anuluj"))
        {
            requestLibreInstallConfirmation = false;
            libreInstallIsReinstall = false;
        }

        ImGui.SameLine();

        string confirmLabel =
            libreInstallIsReinstall
                ? "Przeinstaluj"
                : "Zainstaluj";

        if (ImGui.Button(
                confirmLabel))
        {
            bool reinstall =
                libreInstallIsReinstall;

            requestLibreInstallConfirmation = false;
            libreInstallIsReinstall = false;

            if (libreTranslateRuntimeManager is not null)
            {
                _ = libreTranslateRuntimeManager.InstallAsync(
                    reinstall: reinstall);
            }
        }

        ImGui.PopStyleVar();
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

    private void DrawInformationTab()
    {
        bool forceSectionLayout =
            resetInformationSections;

        string providerName =
            configuration.SelectedTranslationProvider
                == TranslationProvider.LibreTranslate
                    ? "LibreTranslate"
                    : "OpenAI";

        DrawInformationSectionTitle(
            "Status");

        DrawInformationStatusLine(
            "Silnik tłumaczeń",
            providerName);

        DrawInformationStatusLine(
            "Lorekeeper Cloud",
            configuration.CloudEnabled
                ? "Włączony"
                : "Wyłączony");

        if (libreTranslateRuntimeManager is not null)
        {
            DrawInformationStatusLine(
                "LibreTranslate",
                libreTranslateRuntimeManager.StatusText);
        }

        ImGui.Spacing();
        ImGui.Spacing();

        DrawInformationExpandableSection(
            "Szybki start",
            defaultOpen: true,
            forceState: forceSectionLayout,
            () =>
            {
                DrawInformationStep(
                    "1",
                    "W Ustawieniach wybierz OpenAI lub LibreTranslate.");

                DrawInformationStep(
                    "2",
                    "Dla OpenAI wpisz klucz API. Dla LibreTranslate uruchom lokalną instalację.");

                DrawInformationStep(
                    "3",
                    "Rozpocznij dialog z NPC - Lorekeeper pokaże polskie tłumaczenie automatycznie.");
            });

        ImGui.Spacing();

        DrawInformationExpandableSection(
            "Prywatność",
            defaultOpen: false,
            forceState: forceSectionLayout,
            () =>
            {
                ImGui.TextWrapped(
                    "Klucz OpenAI API jest przechowywany lokalnie i nie jest wysyłany do Lorekeeper Cloud.");

                ImGui.Spacing();

                ImGui.TextWrapped(
                    "Przy OpenAI treść dialogu i potrzebny kontekst są wysyłane do OpenAI w celu wykonania tłumaczenia.");

                ImGui.Spacing();

                ImGui.TextWrapped(
                    "LibreTranslate działa lokalnie na Twoim komputerze.");
            });

        ImGui.Spacing();

        DrawInformationExpandableSection(
            "Ważne informacje",
            defaultOpen: false,
            forceState: forceSectionLayout,
            () =>
            {
                ImGui.TextWrapped(
                    "Lorekeeper tłumaczy obsługiwane dialogi NPC i cinematic - nie cały interfejs gry.");

                ImGui.Spacing();

                ImGui.TextWrapped(
                    "Pierwsze tłumaczenie nowej kwestii może potrwać dłużej. Gotowe wpisy z pamięci lokalnej lub Cloud pojawiają się szybciej.");
            });

        resetInformationSections = false;
    }

    private static void DrawInformationExpandableSection(
        string title,
        bool defaultOpen,
        bool forceState,
        Action drawContent)
    {
        ImGui.PushStyleVar(
            ImGuiStyleVar.FramePadding,
            new Vector2(
                7.0f,
                4.0f));

        ImGui.SetNextItemOpen(
            defaultOpen,
            forceState
                ? ImGuiCond.Always
                : ImGuiCond.FirstUseEver);

        bool expanded =
            ImGui.CollapsingHeader(
                title);

        ImGui.PopStyleVar();

        if (!expanded)
        {
            return;
        }

        ImGui.Spacing();
        ImGui.Indent(12.0f);
        drawContent();
        ImGui.Unindent(12.0f);
        ImGui.Spacing();
    }

    internal void ResetInformationSections()
    {
        resetInformationSections = true;
    }

    private static void DrawInformationStep(
        string number,
        string text)
    {
        ImGui.TextDisabled(
            $"{number}.");

        ImGui.SameLine();

        ImGui.TextWrapped(
            text);

        ImGui.Spacing();
    }

    private void DrawInformationSectionTitle(
        string title)
    {
        ImGui.TextUnformatted(
            title);

        ImGui.Spacing();
    }

    private void DrawInformationStatusLine(
        string label,
        string value)
    {
        ImGui.TextDisabled(
            $"{label}:");

        ImGui.SameLine();

        ImGui.TextUnformatted(
            value);
    }

    private static void DrawInformationSeparator()
    {
        ImGui.Separator();
        ImGui.Spacing();
    }

    private void DrawAuthorTab()
    {
        const float contentLogoSize = 168.0f;
        const float socialButtonWidth = 120.0f;
        const float socialButtonGap = 8.0f;

        float availableWidth =
            ImGui.GetContentRegionAvail().X;

        var logoWrap =
            logoTexture?.GetWrapOrDefault();

        float logoOffset =
            MathF.Max(
                0.0f,
                (availableWidth - contentLogoSize) * 0.5f);

        ImGui.SetCursorPosX(
            ImGui.GetCursorPosX() + logoOffset);

        if (logoWrap is not null)
        {
            ImGui.Image(
                logoWrap.Handle,
                new Vector2(
                    contentLogoSize,
                    contentLogoSize));
        }
        else
        {
            ImGui.Button(
                "LK##AuthorLogo",
                new Vector2(
                    contentLogoSize,
                    contentLogoSize));
        }

        ImGui.Spacing();

        DrawCenteredAuthorText(
            $"Lorekeeper {GetPluginVersion()}");

        ImGui.Spacing();

        DrawCenteredAuthorText(
            "Tłumaczenie dialogów Final Fantasy XIV z angielskiego na polski.",
            disabled: true);

        ImGui.Spacing();
        ImGui.Spacing();

        DrawCenteredAuthorText(
            "Stworzony i rozwijany przez Heiyeshi");

        ImGui.Spacing();

        float buttonsWidth =
            socialButtonWidth * 3.0f + socialButtonGap * 2.0f;

        float buttonsOffset =
            MathF.Max(
                0.0f,
                (availableWidth - buttonsWidth) * 0.5f);

        ImGui.SetCursorPosX(
            ImGui.GetCursorPosX() + buttonsOffset);

        ImGui.PushStyleVar(
            ImGuiStyleVar.FrameRounding,
            SettingsFrameRounding);

        if (ImGui.Button(
                "Twitch",
                new Vector2(
                    socialButtonWidth,
                    0.0f)))
        {
            global::Dalamud.Utility.Util.OpenLink(
                "https://www.twitch.tv/heiyeshi");
        }

        ImGui.SameLine(
            0.0f,
            socialButtonGap);

        if (ImGui.Button(
                "Discord",
                new Vector2(
                    socialButtonWidth,
                    0.0f)))
        {
            global::Dalamud.Utility.Util.OpenLink(
                "https://discord.gg/8NHhFsRed5");
        }

        ImGui.SameLine(
            0.0f,
            socialButtonGap);

        if (ImGui.Button(
                "Wsparcie",
                new Vector2(
                    socialButtonWidth,
                    0.0f)))
        {
            global::Dalamud.Utility.Util.OpenLink(
                "https://tipply.pl/@heiyeshi");
        }

        ImGui.PopStyleVar();

        ImGui.Spacing();

        DrawCenteredAuthorText(
            "Yura Asahi  •  Raiden",
            disabled: true);

        ImGui.Spacing();

        DrawCenteredAuthorText(
            "Problemy, sugestie i kontakt: Twitch, Discord lub gra.",
            disabled: true);

        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        const string projectInfoLabel =
            "O projekcie";

        Vector2 projectInfoLabelSize =
            ImGui.CalcTextSize(
                projectInfoLabel);

        const float infoIconWidth = 16.0f;
        float infoRowWidth =
            projectInfoLabelSize.X
            + ImGui.GetStyle().ItemSpacing.X
            + infoIconWidth;

        float infoOffset =
            MathF.Max(
                0.0f,
                (availableWidth - infoRowWidth) * 0.5f);

        ImGui.SetCursorPosX(
            ImGui.GetCursorPosX() + infoOffset);

        ImGui.TextDisabled(
            projectInfoLabel);

        ImGui.SameLine();

        DrawInfoTooltip(
            "Lorekeeper jest rozwijany i testowany bezpośrednio w Final Fantasy XIV. " +
            "Projekt łączy tłumaczenie OpenAI, opcjonalny lokalny LibreTranslate, pamięć tłumaczeń, " +
            "Lorekeeper Cloud oraz integrację z OBS.\n\n" +
            "Przy projektowaniu, implementacji i iteracyjnym rozwijaniu Lorekeepera wykorzystywane są " +
            "narzędzia AI jako wsparcie programistyczne. Kierunek projektu, testy i decyzje dotyczące " +
            "działania pluginu należą do autora.");
    }

    private static void DrawCenteredAuthorText(
        string text,
        bool disabled = false)
    {
        float availableWidth =
            ImGui.GetContentRegionAvail().X;

        float textWidth =
            ImGui.CalcTextSize(text).X;

        float offset =
            MathF.Max(
                0.0f,
                (availableWidth - textWidth) * 0.5f);

        ImGui.SetCursorPosX(
            ImGui.GetCursorPosX() + offset);

        if (disabled)
        {
            ImGui.TextDisabled(text);
            return;
        }

        ImGui.TextUnformatted(text);
    }

    private void DrawOpenAiTab()
    {
        ImGui.Text(
            "OpenAI API Key");

        ImGui.SameLine();
        DrawInfoTooltip(
            "Klucz utworzysz na platform.openai.com w sekcji API Keys. " +
            "Wybierz Create new secret key, skopiuj go po utworzeniu " +
            "i wklej tutaj. Pełny klucz jest wyświetlany tylko podczas tworzenia.");

        ImGui.SameLine();
        DrawInfoTooltip(
            "Klucz OpenAI API zostanie zapisany lokalnie w konfiguracji pluginu.");

        const float saveButtonWidth =
            80.0f;

        const float controlGap =
            8.0f;

        float keyInputWidth =
            MathF.Max(
                220.0f,
                GetSettingsControlWidth()
                - saveButtonWidth
                - controlGap);

        ImGui.SetNextItemWidth(
            keyInputWidth);

        ImGui.InputText(
            "##OpenAiApiKey",
            ref apiKey,
            ApiKeyInputCapacity,
            ImGuiInputTextFlags.Password);

        ImGui.SameLine(
            0.0f,
            controlGap);

        ImGui.PushStyleVar(
            ImGuiStyleVar.FrameRounding,
            SettingsFrameRounding);

        if (ImGui.Button(
                "Zapisz",
                new Vector2(
                    saveButtonWidth,
                    0.0f)))
        {
            SaveApiKey();

            statusMessage =
                "Zapisano klucz OpenAI API.";
        }

        ImGui.PopStyleVar();

        ImGui.Spacing();

        ImGui.Text(
            "Model");

        DrawOpenAiModelCombo();

        ImGui.Spacing();
    }

    private static void DrawOpenAiUsageLine(
        string label,
        decimal costUsd)
    {
        ImGui.TextDisabled(
            $"{label}:");

        ImGui.SameLine();

        ImGui.TextUnformatted(
            $"${costUsd:0.000000}");
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

                configuration.OpenAiModel =
                    modelId;

                configuration.Save();
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

    private void DrawBubbleSettingsTab()
    {
        DrawBubbleStyleSelector(
            configuration.NormalDialogueBubbleStyle,
            "Normal");

        ImGui.Spacing();

        DrawDialoguePreviewControls();

        if (configuration.NormalDialogueBubbleStyle
            == DialogueBubbleStyle.Classic)
        {
            ImGui.Spacing();

            DrawClassicBubbleOpacityControls();
        }

        if (configuration.NormalDialogueBubbleStyle
            == DialogueBubbleStyle.Relic)
        {
            ImGui.Spacing();

            bool coverOriginal =
                configuration.CoverOriginalNormalDialogue;

            if (ImGui.Checkbox(
                    "Zasłaniaj oryginalny dymek gry",
                    ref coverOriginal))
            {
                configuration.CoverOriginalNormalDialogue =
                    coverOriginal;

                configuration.Save();
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextUnformatted(
            "Ustawienia");

        ImGui.Spacing();

        DrawDialogueDisplayControls();
    }

    private void DrawClassicBubbleOpacityControls()
    {
        int opacityPercent =
            (int)MathF.Round(
                Math.Clamp(
                    configuration.ClassicBubbleOpacity,
                    0.20f,
                    1.0f)
                * 100.0f);

        const float resetButtonWidth =
            72.0f;

        const float controlGap =
            8.0f;

        const float sectionIndent =
            12.0f;

        float sectionWidth =
            MathF.Max(
                280.0f,
                GetSettingsControlWidth()
                * 0.60f);

        ImGui.Indent(
            sectionIndent);

        if (ImGui.BeginTable(
                "ClassicBubbleOpacityTable",
                1,
                ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn(
                "ClassicBubbleOpacityColumn",
                ImGuiTableColumnFlags.WidthFixed,
                sectionWidth);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();

            ImGui.TextUnformatted(
                "Przezroczystość klasycznego dymka");

            float sliderWidth =
                MathF.Max(
                    120.0f,
                    sectionWidth
                    - resetButtonWidth
                    - controlGap);

            ImGui.SetNextItemWidth(
                sliderWidth);

            if (ImGui.SliderInt(
                    "##ClassicBubbleOpacity",
                    ref opacityPercent,
                    20,
                    100,
                    "%d%%"))
            {
                configuration.ClassicBubbleOpacity =
                    opacityPercent / 100.0f;

                configuration.Save();
            }

            ImGui.SameLine(
                0.0f,
                controlGap);

            if (ImGui.Button(
                    "Reset##ClassicBubbleOpacity",
                    new Vector2(
                        resetButtonWidth,
                        0.0f)))
            {
                configuration.ClassicBubbleOpacity =
                    0.75f;

                configuration.Save();
            }

            ImGui.EndTable();
        }

        ImGui.Unindent(
            sectionIndent);
    }

    private void DrawDialogueDisplayControls()
    {
        const float resetButtonWidth =
            72.0f;

        const float controlGap =
            8.0f;

        const float sectionIndent =
            20.0f;

        float sectionWidth =
            MathF.Max(
                280.0f,
                GetSettingsControlWidth()
                * 0.60f);

        ImGui.Indent(
            sectionIndent);

        if (ImGui.BeginTable(
                "DialogueDisplayControlsTable",
                1,
                ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn(
                "DialogueDisplayControlsColumn",
                ImGuiTableColumnFlags.WidthFixed,
                sectionWidth);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();

            float sliderWidth =
                MathF.Max(
                    120.0f,
                    sectionWidth
                    - resetButtonWidth
                    - controlGap);

            ImGui.PushStyleVar(
                ImGuiStyleVar.FramePadding,
                new Vector2(
                    7.0f,
                    4.0f));

            ImGui.SetNextItemOpen(
                false,
                ImGuiCond.FirstUseEver);

            bool normalExpanded =
                ImGui.CollapsingHeader(
                    "Dialog zwykły");

            ImGui.PopStyleVar();

            if (normalExpanded)
            {
                ImGui.Spacing();

                float normalFontSize =
                    Math.Clamp(
                        configuration.NormalDialogueFontSize,
                        16.0f,
                        28.0f);

                ImGui.TextDisabled(
                    "Rozmiar tekstu");

                ImGui.SetNextItemWidth(
                    sliderWidth);

                if (ImGui.SliderFloat(
                        "##NormalDialogueFontSize",
                        ref normalFontSize,
                        16.0f,
                        28.0f,
                        "%.0f px"))
                {
                    configuration.NormalDialogueFontSize =
                        normalFontSize;

                    configuration.Save();
                }

                ImGui.SameLine(
                    0.0f,
                    controlGap);

                if (ImGui.Button(
                        "Reset##NormalDialogueFontSize",
                        new Vector2(
                            resetButtonWidth,
                            0.0f)))
                {
                    configuration.NormalDialogueFontSize =
                        20.0f;

                    configuration.Save();
                }

                float normalVerticalOffset =
                    Math.Clamp(
                        configuration.NormalDialogueVerticalOffset,
                        -160.0f,
                        160.0f);

                ImGui.TextDisabled(
                    "Pozycja pionowa");

                ImGui.SetNextItemWidth(
                    sliderWidth);

                if (ImGui.SliderFloat(
                        "##NormalDialogueVerticalOffset",
                        ref normalVerticalOffset,
                        -160.0f,
                        160.0f,
                        "%+.0f px"))
                {
                    configuration.NormalDialogueVerticalOffset =
                        normalVerticalOffset;

                    configuration.Save();
                }

                ImGui.SameLine(
                    0.0f,
                    controlGap);

                if (ImGui.Button(
                        "Reset##NormalDialogueVerticalOffset",
                        new Vector2(
                            resetButtonWidth,
                            0.0f)))
                {
                    configuration.NormalDialogueVerticalOffset =
                        0.0f;

                    configuration.Save();
                }

                ImGui.Spacing();
            }

            ImGui.Spacing();

            ImGui.PushStyleVar(
                ImGuiStyleVar.FramePadding,
                new Vector2(
                    7.0f,
                    4.0f));

            ImGui.SetNextItemOpen(
                false,
                ImGuiCond.FirstUseEver);

            bool cinematicExpanded =
                ImGui.CollapsingHeader(
                    "Cinematic");

            ImGui.PopStyleVar();

            if (cinematicExpanded)
            {
                ImGui.Spacing();

                float cinematicFontSize =
                    Math.Clamp(
                        configuration.CinematicFontSize,
                        22.0f,
                        40.0f);

                ImGui.TextDisabled(
                    "Rozmiar tekstu");

                ImGui.SetNextItemWidth(
                    sliderWidth);

                if (ImGui.SliderFloat(
                        "##CinematicFontSize",
                        ref cinematicFontSize,
                        22.0f,
                        40.0f,
                        "%.0f px"))
                {
                    configuration.CinematicFontSize =
                        cinematicFontSize;

                    configuration.Save();
                }

                ImGui.SameLine(
                    0.0f,
                    controlGap);

                if (ImGui.Button(
                        "Reset##CinematicFontSize",
                        new Vector2(
                            resetButtonWidth,
                            0.0f)))
                {
                    configuration.CinematicFontSize =
                        30.0f;

                    configuration.Save();
                }

                float cinematicVerticalOffset =
                    Math.Clamp(
                        configuration.CinematicVerticalOffset,
                        -160.0f,
                        160.0f);

                ImGui.TextDisabled(
                    "Pozycja pionowa");

                ImGui.SetNextItemWidth(
                    sliderWidth);

                if (ImGui.SliderFloat(
                        "##CinematicVerticalOffset",
                        ref cinematicVerticalOffset,
                        -160.0f,
                        160.0f,
                        "%+.0f px"))
                {
                    configuration.CinematicVerticalOffset =
                        cinematicVerticalOffset;

                    configuration.Save();
                }

                ImGui.SameLine(
                    0.0f,
                    controlGap);

                if (ImGui.Button(
                        "Reset##CinematicVerticalOffset",
                        new Vector2(
                            resetButtonWidth,
                            0.0f)))
                {
                    configuration.CinematicVerticalOffset =
                        0.0f;

                    configuration.Save();
                }

                ImGui.Spacing();
            }

            ImGui.EndTable();
        }

        ImGui.Unindent(
            sectionIndent);
    }

    private void DrawDialoguePreviewControls()
    {
        bool normalActive =
            plugin.IsDialoguePreviewActive(
                DialogueDisplayKind.Normal);

        bool cinematicActive =
            plugin.IsDialoguePreviewActive(
                DialogueDisplayKind.Cinematic);

        const float gap =
            8.0f;

        float availableWidth =
            GetSettingsControlWidth();

        float normalWidth =
            MathF.Max(
                112.0f,
                availableWidth * 0.25f);

        float cinematicWidth =
            MathF.Max(
                128.0f,
                availableWidth * 0.28f);

        float hideWidth =
            72.0f;

        float resetAllWidth =
            MathF.Max(
                116.0f,
                availableWidth
                - normalWidth
                - cinematicWidth
                - hideWidth
                - gap * 3.0f);

        if (normalActive)
        {
            ImGui.PushStyleColor(
                ImGuiCol.Button,
                ImGui.GetStyle().Colors[
                    (int)ImGuiCol.ButtonActive]);
        }

        if (ImGui.Button(
                "Podgląd zwykły",
                new Vector2(
                    normalWidth,
                    0.0f)))
        {
            plugin.ShowDialoguePreview(
                DialogueDisplayKind.Normal);
        }

        if (normalActive)
        {
            ImGui.PopStyleColor();
        }

        ImGui.SameLine(
            0.0f,
            gap);

        if (cinematicActive)
        {
            ImGui.PushStyleColor(
                ImGuiCol.Button,
                ImGui.GetStyle().Colors[
                    (int)ImGuiCol.ButtonActive]);
        }

        if (ImGui.Button(
                "Podgląd cinematic",
                new Vector2(
                    cinematicWidth,
                    0.0f)))
        {
            plugin.ShowDialoguePreview(
                DialogueDisplayKind.Cinematic);
        }

        if (cinematicActive)
        {
            ImGui.PopStyleColor();
        }

        ImGui.SameLine(
            0.0f,
            gap);

        if (ImGui.Button(
                "Ukryj",
                new Vector2(
                    hideWidth,
                    0.0f)))
        {
            plugin.HideDialoguePreview();
        }

        ImGui.SameLine(
            0.0f,
            gap);

        if (ImGui.Button(
                "Resetuj wszystko",
                new Vector2(
                    resetAllWidth,
                    0.0f)))
        {
            ResetAllBubbleSliders();
        }
    }

    private void ResetAllBubbleSliders()
    {
        configuration.ClassicBubbleOpacity =
            0.75f;

        configuration.NormalDialogueFontSize =
            20.0f;

        configuration.NormalDialogueVerticalOffset =
            0.0f;

        configuration.CinematicFontSize =
            30.0f;

        configuration.CinematicVerticalOffset =
            0.0f;

        configuration.Save();
    }

    private void DrawBubbleStyleSelector(
        DialogueBubbleStyle selectedStyle,
        string idSuffix)
    {
        float availableWidth =
            GetSettingsControlWidth();

        const float gap = 8.0f;

        float cardWidth =
            MathF.Max(
                130.0f,
                (availableWidth - gap * 2.0f) / 3.0f);

        DrawBubbleStyleCard(
            selectedStyle,
            DialogueBubbleStyle.Classic,
            "Klasyczny",
            idSuffix,
            cardWidth);

        ImGui.SameLine(
            0.0f,
            gap);

        DrawBubbleStyleCard(
            selectedStyle,
            DialogueBubbleStyle.Hud,
            "Panel HUD",
            idSuffix,
            cardWidth);

        ImGui.SameLine(
            0.0f,
            gap);

        DrawBubbleStyleCard(
            selectedStyle,
            DialogueBubbleStyle.Relic,
            "Relikt",
            idSuffix,
            cardWidth);
    }

    private void DrawBubbleStyleCard(
        DialogueBubbleStyle selectedStyle,
        DialogueBubbleStyle style,
        string label,
        string idSuffix,
        float width)
    {
        bool selected =
            selectedStyle == style;

        Vector2 start =
            ImGui.GetCursorScreenPos();

        ImGui.PushID(
            $"{idSuffix}{style}");

        bool clicked =
            ImGui.InvisibleButton(
                "##BubbleStyleCard",
                new Vector2(
                    width,
                    BubbleStyleCardHeight));

        bool hovered =
            ImGui.IsItemHovered();

        var drawList =
            ImGui.GetWindowDrawList();

        Vector4 background =
            selected
                ? ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive]
                : hovered
                    ? ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBgHovered]
                    : ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg];

        Vector4 border =
            selected
                ? ImGui.GetStyle().Colors[(int)ImGuiCol.CheckMark]
                : ImGui.GetStyle().Colors[(int)ImGuiCol.Border];

        drawList.AddRectFilled(
            start,
            start + new Vector2(
                width,
                BubbleStyleCardHeight),
            ImGui.GetColorU32(background),
            SettingsFrameRounding);

        drawList.AddRect(
            start,
            start + new Vector2(
                width,
                BubbleStyleCardHeight),
            ImGui.GetColorU32(border),
            SettingsFrameRounding,
            ImDrawFlags.None,
            selected
                ? 1.5f
                : 1.0f);

        drawList.AddText(
            start + new Vector2(
                10.0f,
                9.0f),
            ImGui.GetColorU32(
                ImGuiCol.Text),
            label);

        if (selected)
        {
            drawList.AddCircleFilled(
                start + new Vector2(
                    width - 13.0f,
                    14.0f),
                4.0f,
                ImGui.GetColorU32(
                    ImGuiCol.CheckMark),
                16);
        }

        DrawBubbleStylePreview(
            style,
            start + new Vector2(
                10.0f,
                45.0f),
            width - 20.0f);

        if (clicked)
        {
            configuration.NormalDialogueBubbleStyle =
                style;

            if (style != DialogueBubbleStyle.Relic)
            {
                configuration.CoverOriginalNormalDialogue =
                    false;
            }

            configuration.Save();
        }

        ImGui.PopID();
    }

    private static void DrawBubbleStylePreview(
        DialogueBubbleStyle style,
        Vector2 start,
        float width)
    {
        var drawList =
            ImGui.GetWindowDrawList();

        float previewWidth =
            MathF.Max(
                60.0f,
                width);

        const float previewHeight = 22.0f;

        uint lineColor =
            ImGui.GetColorU32(
                ImGuiCol.TextDisabled);

        if (style == DialogueBubbleStyle.Classic)
        {
            drawList.AddRectFilled(
                start,
                start + new Vector2(
                    previewWidth,
                    previewHeight),
                ImGui.GetColorU32(
                    new Vector4(
                        0.08f,
                        0.08f,
                        0.09f,
                        0.78f)),
                11.0f);

            drawList.AddLine(
                start + new Vector2(
                    9.0f,
                    10.0f),
                start + new Vector2(
                    previewWidth * 0.58f,
                    10.0f),
                lineColor,
                1.0f);

            return;
        }

        if (style == DialogueBubbleStyle.Hud)
        {
            uint hudBorder =
                ImGui.GetColorU32(
                    ImGuiCol.Border);

            drawList.AddRectFilled(
                start,
                start + new Vector2(
                    previewWidth,
                    previewHeight),
                ImGui.GetColorU32(
                    new Vector4(
                        0.07f,
                        0.06f,
                        0.07f,
                        0.92f)),
                1.0f);

            drawList.AddRect(
                start,
                start + new Vector2(
                    previewWidth,
                    previewHeight),
                hudBorder,
                1.0f,
                ImDrawFlags.None,
                1.0f);

            drawList.AddLine(
                start + new Vector2(
                    8.0f,
                    10.0f),
                start + new Vector2(
                    previewWidth * 0.62f,
                    10.0f),
                lineColor,
                1.0f);

            return;
        }

        drawList.AddRectFilled(
            start + new Vector2(
                2.0f,
                1.0f),
            start + new Vector2(
                previewWidth - 2.0f,
                previewHeight - 1.0f),
            ImGui.GetColorU32(
                new Vector4(
                    0.91f,
                    0.89f,
                    0.82f,
                    0.96f)),
            3.0f);

        uint relicEdge =
            ImGui.GetColorU32(
                new Vector4(
                    0.48f,
                    0.46f,
                    0.42f,
                    0.70f));

        drawList.AddLine(
            start + new Vector2(
                4.0f,
                2.0f),
            start + new Vector2(
                previewWidth * 0.32f,
                0.0f),
            relicEdge,
            1.0f);

        drawList.AddLine(
            start + new Vector2(
                previewWidth * 0.40f,
                1.0f),
            start + new Vector2(
                previewWidth - 5.0f,
                3.0f),
            relicEdge,
            1.0f);

        drawList.AddLine(
            start + new Vector2(
                9.0f,
                10.0f),
            start + new Vector2(
                previewWidth * 0.58f,
                10.0f),
            ImGui.GetColorU32(
                new Vector4(
                    0.18f,
                    0.17f,
                    0.15f,
                    0.78f)),
            1.0f);
    }

    private void DrawUiTab()
    {
        bool isMovable =
            configuration.IsConfigWindowMovable;

        if (ImGui.Checkbox(
                "Pozwól przesuwać okno /lore",
                ref isMovable))
        {
            configuration.IsConfigWindowMovable =
                isMovable;

            configuration.Save();
        }
    }

    private void SaveApiKey()
    {
        configuration.OpenAiApiKey =
            apiKey.Trim();

        configuration.Save();
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
