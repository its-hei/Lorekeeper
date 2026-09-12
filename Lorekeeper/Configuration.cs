using Dalamud.Configuration;

namespace Lorekeeper;

public enum DialogueBubbleStyle
{
    Classic,
    Relic,
    Hud
}

public enum DialogueDisplayKind
{
    Normal,
    Cinematic
}

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool IsConfigWindowMovable { get; set; } = true;

    public string OpenAiApiKey { get; set; } = string.Empty;

    public string OpenAiModel { get; set; } = "gpt-4o-mini";

    public TranslationProvider SelectedTranslationProvider { get; set; } =
        TranslationProvider.OpenAI;

    public DialogueBubbleStyle NormalDialogueBubbleStyle { get; set; } =
        DialogueBubbleStyle.Classic;

    public DialogueBubbleStyle CinematicDialogueBubbleStyle { get; set; } =
        DialogueBubbleStyle.Classic;

    public bool CoverOriginalNormalDialogue { get; set; } = false;

    // Pole pozostaje wyłącznie dla zgodności ze starszym configiem.
    // Od 1.3.1.9 opcje/menu wyboru nie są tłumaczone.
    public bool TranslateDialogueChoices { get; set; } = false;

    // Ustawienia wyglądu napisów.
    public float NormalDialogueFontSize { get; set; } = 20.0f;

    public float CinematicFontSize { get; set; } = 30.0f;

    // Dodatnia wartość przesuwa napis w dół, ujemna w górę.
    public float NormalDialogueVerticalOffset { get; set; } = 0.0f;

    public float CinematicVerticalOffset { get; set; } = 0.0f;

    // Dotyczy wyłącznie klasycznego dymka.
    public float ClassicBubbleOpacity { get; set; } = 0.75f;

    // Lorekeeper Cloud jest opcjonalny. Dopóki CloudApiUrl jest pusty,
    // plugin działa dokładnie jak wcześniej - wyłącznie lokalnie.
    public bool CloudEnabled { get; set; } = true;

    public string CloudApiUrl { get; set; } =
        "https://lorekeeper-cloud.heiyeshi.workers.dev";

    public int CloudLookupTimeoutMilliseconds { get; set; } = 1800;

    public string CloudClientId { get; set; } =
        System.Guid.NewGuid().ToString("N");

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
