using Dalamud.Configuration;
using Dalamud.Plugin;

namespace AuxMarketboard;

public sealed class Configuration : IPluginConfiguration
{
    public const int CurrentVersion = 3;
    public const int MaxQuantity = 999_999;
    public const int MaxWorldLimit = 5;

    public int Version { get; set; } = CurrentVersion;
    public ScopeMode ScopeMode { get; set; } = ScopeMode.Region;
    public int MaxWorlds { get; set; }
    public string LastWorld { get; set; } = string.Empty;
    public string LastDataCenter { get; set; } = string.Empty;
    public string LastRegion { get; set; } = string.Empty;
    public List<ShoppingEntry> Items { get; set; } = new();

    [System.Text.Json.Serialization.JsonIgnore]
    private IDalamudPluginInterface? PluginInterface { get; set; }

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
        PluginInterface = pluginInterface;
    }

    public bool Migrate()
    {
        var changed = Version != CurrentVersion;
        Version = CurrentVersion;

        if (!Enum.IsDefined(ScopeMode))
        {
            ScopeMode = ScopeMode.Region;
            changed = true;
        }

        var clampedWorlds = Math.Clamp(MaxWorlds, 0, MaxWorldLimit);
        if (clampedWorlds != MaxWorlds)
        {
            MaxWorlds = clampedWorlds;
            changed = true;
        }

        var cleaned = new List<ShoppingEntry>();
        foreach (var entry in Items ?? new List<ShoppingEntry>())
        {
            if (entry is null || entry.ItemId == 0)
            {
                continue;
            }

            var existing = cleaned.FirstOrDefault(x => x.ItemId == entry.ItemId);
            if (existing is null)
            {
                cleaned.Add(new ShoppingEntry
                {
                    ItemId = entry.ItemId,
                    Quantity = Math.Clamp(entry.Quantity, 1, MaxQuantity),
                    HqOnly = entry.HqOnly,
                });
            }
            else
            {
                existing.Quantity = Math.Clamp(existing.Quantity + entry.Quantity, 1, MaxQuantity);
            }
        }

        if (cleaned.Count != Items?.Count)
        {
            changed = true;
        }

        Items = cleaned;
        return changed;
    }

    public void Save()
    {
        PluginInterface?.SavePluginConfig(this);
    }
}
