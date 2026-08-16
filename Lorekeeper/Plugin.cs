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

    [PluginService]
    internal static IObjectTable ObjectTable { get; private set; } = null!;

    [PluginService]
    internal static ITextureProvider TextureProvider { get; private set; } = null!;

    private readonly WindowSystem windowSystem = new("Lorekeeper");
    private readonly ConfigWindow configWindow;
    private readonly MainWindow mainWindow;
    private readonly DialogueEngine dialogueEngine;
    private readonly PlayerTranslationContextProvider translationContextProvider;
    private readonly NpcKnowledgeStore npcKnowledgeStore;
    private readonly NpcSexResolver npcSexResolver;
    private readonly TerminologyStore terminologyStore;
    private readonly TerminologyProposalStore terminologyProposalStore;
    private readonly ConversationMemory conversationMemory;
    private readonly ObsOverlayServer obsOverlayServer;
    private readonly LibreTranslateRuntimeManager libreTranslateRuntimeManager;
    private readonly LorekeeperCloudClient cloudClient;

    public Plugin()
    {
        Log.Information("LOREKEEPER BUILD: KNOWLEDGE TEST 2");
        Log.Information(
            $"LOREKEEPER DLL: {typeof(Plugin).Assembly.Location}");

        Configuration =
            PluginInterface.GetPluginConfig() as Configuration
            ?? new Configuration();

        PluginInterface.UiBuilder.DisableCutsceneUiHide = true;

        translationContextProvider =
            new PlayerTranslationContextProvider(PlayerState);

        var lorekeeperLogger =
            new DalamudLorekeeperLogger(Log);

        string knowledgePath = Path.Combine(
            PluginInterface.ConfigDirectory.FullName,
            "knowledge.json");

        npcKnowledgeStore = new NpcKnowledgeStore(
            knowledgePath,
            lorekeeperLogger);

        npcSexResolver = new NpcSexResolver(
            ObjectTable,
            Log);

        Log.Information($"KNOWLEDGE FILE: {knowledgePath}");

        string terminologyPath = Path.Combine(
            PluginInterface.ConfigDirectory.FullName,
            "terminology.json");

        terminologyStore = new TerminologyStore(
            terminologyPath,
            lorekeeperLogger);

        Log.Information(
            $"TERMINOLOGY FILE: {terminologyPath}");

        string terminologyProposalsPath = Path.Combine(
            PluginInterface.ConfigDirectory.FullName,
            "terminology-proposals.json");

        terminologyProposalStore =
            new TerminologyProposalStore(
                terminologyProposalsPath,
                lorekeeperLogger);

        Log.Information(
            $"TERMINOLOGY PROPOSALS FILE: " +
            $"{terminologyProposalsPath}");

        libreTranslateRuntimeManager =
            new LibreTranslateRuntimeManager(
                PluginInterface.ConfigDirectory.FullName,
                lorekeeperLogger);

        EnsureCloudClientId();

        cloudClient =
            new LorekeeperCloudClient(
                Configuration,
                terminologyStore,
                lorekeeperLogger);

        configWindow = new ConfigWindow(
            this,
            terminologyProposalStore,
            terminologyStore,
            libreTranslateRuntimeManager);

        mainWindow = new MainWindow(this);

        windowSystem.AddWindow(configWindow);
        windowSystem.AddWindow(mainWindow);

        conversationMemory =
            new ConversationMemory(20);

        dialogueEngine = new DialogueEngine(CreateTranslator());

        obsOverlayServer = new ObsOverlayServer(
            () => dialogueEngine.CurrentDialogue,
            lorekeeperLogger);

        RegisterCommand();
        RegisterUiCallbacks();
        RegisterTalkListeners();

        // Jeżeli lokalny LibreTranslate był wcześniej zainstalowany,
        // uruchamiamy go automatycznie po starcie pluginu.
        _ = libreTranslateRuntimeManager.StartIfInstalledAsync();

        Log.Information(
            $"Plugin {PluginInterface.Manifest.Name} został uruchomiony.");
    }

    public Configuration Configuration { get; }

    internal DialogueSnapshot CurrentDialogue =>
        dialogueEngine.CurrentDialogue;

    public void Dispose()
    {
        obsOverlayServer.Dispose();
        cloudClient.Dispose();
        libreTranslateRuntimeManager.Dispose();

        UnregisterTalkListeners();
        UnregisterUiCallbacks();
        CommandManager.RemoveHandler(CommandName);

        windowSystem.RemoveAllWindows();
        configWindow.Dispose();
        mainWindow.Dispose();
    }

    private ITranslator CreateTranslator()
    {
        string openAiCachePath = Path.Combine(
            PluginInterface.ConfigDirectory.FullName,
            "translations.json");

        string libreCachePath = Path.Combine(
            PluginInterface.ConfigDirectory.FullName,
            "translations-libre.json");

        Log.Information($"OPENAI CACHE FILE: {openAiCachePath}");
        Log.Information($"LIBRE CACHE FILE: {libreCachePath}");

        var options = new OpenAiTranslatorOptions(
            Configuration.OpenAiApiKey,
            Configuration.OpenAiModel);

        var translatorLogger =
            new DalamudLorekeeperLogger(Log);

        var openAiTranslator = new Translator(
            new TranslationCache(openAiCachePath),
            options,
            translatorLogger,
            terminologyStore,
            conversationMemory);

        var libreTranslator = new LibreTranslateTranslator(
            new TranslationCache(libreCachePath),
            translatorLogger,
            conversationMemory);

        return new TranslationRouter(
            Configuration,
            openAiTranslator,
            libreTranslator,
            cloudClient,
            translatorLogger);
    }

    private void EnsureCloudClientId()
    {
        if (!string.IsNullOrWhiteSpace(
                Configuration.CloudClientId))
        {
            return;
        }

        Configuration.CloudClientId =
            Guid.NewGuid().ToString("N");

        Configuration.Save();
    }

    private void RegisterCommand()
    {
        CommandManager.AddHandler(
            CommandName,
            new CommandInfo(OnCommand)
            {
                HelpMessage = "Opens the Lorekeeper window."
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
        configWindow.Toggle();
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

        Log.Information(
            $"KNOWLEDGE CHECK: NPC = {npcName}");

        TranslationContext playerContext =
            translationContextProvider.GetCurrent();

        PlayerSex speakerSex =
            ResolveAndRememberSpeakerSex(npcName);

        TranslationContext context =
            new(
                playerContext.PlayerCharacterSex,
                speakerSex);

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

    private PlayerSex ResolveAndRememberSpeakerSex(
        string npcName)
    {
        if (npcSexResolver.TryResolve(
                npcName,
                out NpcResolvedIdentity identity))
        {
            npcKnowledgeStore.RememberSex(
                identity.BaseId,
                identity.Name,
                identity.Sex,
                KnowledgeSource.GameData,
                1.0);

            Log.Information(
                $"KNOWLEDGE SAVED: " +
                $"{identity.Name} " +
                $"[BaseId={identity.BaseId}] = " +
                $"{identity.Sex}");

            return identity.Sex;
        }

        Log.Information(
            $"KNOWLEDGE: Brak aktywnej tożsamości NPC " +
            $"dla nazwy {npcName}. Nie użyto pamięci po nazwie.");

        return PlayerSex.Unknown;
    }

    private void OnTalkClosed(AddonEvent eventType, AddonArgs args)
    {
        dialogueEngine.MarkClosed();
        conversationMemory.Clear();

        Log.Information(
            "CONVERSATION: Pamięć rozmowy została wyczyszczona.");

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
