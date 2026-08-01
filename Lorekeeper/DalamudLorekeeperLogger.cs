using System;
using Dalamud.Plugin.Services;

namespace Lorekeeper.Dalamud;

public sealed class DalamudLorekeeperLogger : ILorekeeperLogger
{
    private readonly IPluginLog pluginLog;

    public DalamudLorekeeperLogger(IPluginLog pluginLog)
    {
        this.pluginLog = pluginLog
            ?? throw new ArgumentNullException(nameof(pluginLog));
    }

    public void Information(string message)
    {
        pluginLog.Information(message);
    }

    public void Warning(string message)
    {
        pluginLog.Warning(message);
    }

    public void Error(Exception exception, string message)
    {
        pluginLog.Error(exception, message);
    }
}
