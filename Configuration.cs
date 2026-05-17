using Dalamud.Configuration;
using Dalamud.Plugin;

namespace AuxMarketboard;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public string WorldOrDataCenter { get; set; } = "Oceania";
    public bool AutoDetectWorldOnStartup { get; set; } = true;
    public bool AutoRefreshEnabled { get; set; } = true;
    public int AutoRefreshIntervalSeconds { get; set; } = 20;
    public bool IncludeNormalQuality { get; set; } = true;
    public bool IncludeHighQuality { get; set; } = true;
    public bool SearchByDataCenter { get; set; } = false;
    public bool SearchByRegion { get; set; } = true;
    public bool GroupedMode { get; set; } = false;

    [System.Text.Json.Serialization.JsonIgnore]
    private IDalamudPluginInterface? PluginInterface { get; set; }

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
        PluginInterface = pluginInterface;
    }

    public void Save()
    {
        PluginInterface?.SavePluginConfig(this);
    }
}
