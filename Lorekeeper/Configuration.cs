using Dalamud.Configuration;

namespace Lorekeeper;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool IsConfigWindowMovable { get; set; } = true;

    public string OpenAiApiKey { get; set; } = string.Empty;

    public string OpenAiModel { get; set; } = "gpt-4o-mini";

    public TranslationProvider SelectedTranslationProvider { get; set; } =
        TranslationProvider.OpenAI;

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
