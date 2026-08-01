using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lorekeeper.Dalamud;
using Lorekeeper.OBS;
using Lorekeeper.Windows;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Lorekeeper;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/lore";
    private const string TalkAddonName = "Talk";

    [PluginService]
    internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    [PluginService]
    internal static ICommandManager CommandManager { get; private set; } = null!;

    [PluginService]
    internal static IPluginLog Log { get; private set; } = null!;

    [PluginService]
    internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;

    [PluginService]
    internal static IPlayerState PlayerState { get; private set; } = null!;

    private readonly WindowSystem windowSystem = new("Lorekeeper");
    private readonly ConfigWindow configWindow;
    private readonly MainWindow mainWindow;
    private readonly DialogueEngine dialogueEngine;
    private readonly PlayerTranslationContextProvider translationContextProvider;
    private readonly ObsOverlayServer obsOverlayServer;

    public Plugin()
    {
        Configuration =
            PluginInterface.GetPluginConfig() as Configuration
            ?? new Configuration();

        PluginInterface.UiBuilder.DisableCutsceneUiHide = true;

        configWindow = new ConfigWindow(this);
        mainWindow = new MainWindow(this);

        windowSystem.AddWindow(configWindow);
        windowSystem.AddWindow(mainWindow);

        translationContextProvider =
            new PlayerTranslationContextProvider(PlayerState);

        dialogueEngine = new DialogueEngine(CreateTranslator());

        obsOverlayServer = new ObsOverlayServer(
            () => dialogueEngine.CurrentDialogue,
            new DalamudLorekeeperLogger(Log));

        RegisterCommand();
        RegisterUiCallbacks();
        RegisterTalkListeners();

        Log.Information(
            $"Plugin {PluginInterface.Manifest.Name} został uruchomiony.");
    }

    public Configuration Configuration { get; }

    internal DialogueSnapshot CurrentDialogue =>
        dialogueEngine.CurrentDialogue;

    public void Dispose()
    {
        obsOverlayServer.Dispose();

        UnregisterTalkListeners();
        UnregisterUiCallbacks();
        CommandManager.RemoveHandler(CommandName);

        windowSystem.RemoveAllWindows();
        configWindow.Dispose();
        mainWindow.Dispose();
    }

    private ITranslator CreateTranslator()
    {
        string cachePath = Path.Combine(
            PluginInterface.ConfigDirectory.FullName,
            "translations.json");

        Log.Information($"CACHE FILE: {cachePath}");

        var options = new OpenAiTranslatorOptions(
            Configuration.OpenAiApiKey,
            Configuration.OpenAiModel);

        var translatorLogger =
            new DalamudLorekeeperLogger(Log);

        return new Translator(
            new TranslationCache(cachePath),
            options,
            translatorLogger);
    }

    private void RegisterCommand()
    {
        CommandManager.AddHandler(
            CommandName,
            new CommandInfo(OnCommand)
            {
                HelpMessage = "Otwiera nakładkę Lorekeeper."
            });
    }

    private void RegisterUiCallbacks()
    {
        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
    }

    private void UnregisterUiCallbacks()
    {
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
    }

    private void RegisterTalkListeners()
    {
        AddonLifecycle.RegisterListener(
            AddonEvent.PostRefresh,
            TalkAddonName,
            OnTalkRefreshed);

        AddonLifecycle.RegisterListener(
            AddonEvent.PreHide,
            TalkAddonName,
            OnTalkClosed);

        AddonLifecycle.RegisterListener(
            AddonEvent.PreClose,
            TalkAddonName,
            OnTalkClosed);

        AddonLifecycle.RegisterListener(
            AddonEvent.PreFinalize,
            TalkAddonName,
            OnTalkClosed);
    }

    private void UnregisterTalkListeners()
    {
        AddonLifecycle.UnregisterListener(OnTalkRefreshed);
        AddonLifecycle.UnregisterListener(OnTalkClosed);
    }

    private void OnCommand(string command, string arguments)
    {
        mainWindow.Toggle();
    }

    private void OnTalkRefreshed(AddonEvent eventType, AddonArgs args)
    {
        if (!TalkDialogueReader.TryRead(
                args,
                out string npcName,
                out string dialogue))
        {
            return;
        }

        dialogueEngine.MarkOpen();

        TranslationContext context =
            translationContextProvider.GetCurrent();

        if (!dialogueEngine.TryProcessDialogue(
                npcName,
                dialogue,
                context,
                out Task<TranslationResult?>? completion)
            || completion is null)
        {
            return;
        }

        mainWindow.IsOpen = true;
        LogTranslationStarted(npcName, dialogue);

        _ = ObserveTranslationAsync(npcName, completion);
    }

    private void OnTalkClosed(AddonEvent eventType, AddonArgs args)
    {
        dialogueEngine.MarkClosed();

        Log.Debug(
            $"Okno {TalkAddonName} zostało zamknięte lub ukryte. " +
            $"Event: {eventType}");
    }

    private async Task ObserveTranslationAsync(
        string npcName,
        Task<TranslationResult?> completion)
    {
        try
        {
            TranslationResult? result = await completion;

            if (result is null)
            {
                Log.Debug("Pominięto spóźnione tłumaczenie.");
                return;
            }

            if (dialogueEngine.CurrentDialogue.IsOpen)
            {
                mainWindow.IsOpen = true;
            }

            LogTranslationCompleted(npcName, result);
        }
        catch (Exception exception)
        {
            Log.Error(
                exception,
                $"Nie udało się przetłumaczyć dialogu NPC: {npcName}");
        }
    }

    private static void LogTranslationStarted(
        string npcName,
        string dialogue)
    {
        Log.Information($"SOURCE: {TalkAddonName}");
        Log.Information($"NPC: {npcName}");
        Log.Information($"ORIGINAL: {dialogue}");
        Log.Information("TRANSLATION: oczekiwanie...");
    }

    private static void LogTranslationCompleted(
        string npcName,
        TranslationResult result)
    {
        Log.Information($"SOURCE: {TalkAddonName}");
        Log.Information($"NPC: {npcName}");
        Log.Information($"TRANSLATION: {result.TranslatedText}");
        Log.Information($"CACHE: {result.FromCache}");
        Log.Information($"INPUT TOKENS: {result.InputTokens}");
        Log.Information($"OUTPUT TOKENS: {result.OutputTokens}");
        Log.Information($"COST: {result.CostUsd:F8} USD");
    }

    private void ToggleConfigUi()
    {
        configWindow.Toggle();
    }

    private void ToggleMainUi()
    {
        mainWindow.Toggle();
    }
}
