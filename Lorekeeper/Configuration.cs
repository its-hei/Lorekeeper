using Dalamud.Configuration;

namespace Lorekeeper;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool IsConfigWindowMovable { get; set; } = true;

    public string OpenAiApiKey { get; set; } = string.Empty;

    public string OpenAiModel { get; set; } = "gpt-5-mini";

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
