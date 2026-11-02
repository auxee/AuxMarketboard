using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using AuxMarketboard.Services;

namespace AuxMarketboard;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private readonly MarketService marketService;
    private readonly UniversalisClient universalisClient;

    public Configuration Configuration { get; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(PluginInterface);
        if (Configuration.Migrate())
        {
            Configuration.Save();
        }

        var version = typeof(Plugin).Assembly.GetName().Version?.ToString(3) ?? "0.2.0";
        universalisClient = new UniversalisClient(version);
        marketService = new MarketService(
            Configuration,
            universalisClient,
            new WorldDirectory(),
            ReadHomeWorld,
            (message, exception) => Log.Error(exception, message));
        marketService.StartDirectoryLoad();
    }

    public void Dispose()
    {
        marketService.Dispose();
        universalisClient.Dispose();
    }

    private static HomeWorld? ReadHomeWorld()
    {
        if (!PlayerState.IsLoaded || !PlayerState.HomeWorld.IsValid)
        {
            return null;
        }

        var world = PlayerState.HomeWorld.Value;
        var dataCenter = world.DataCenter.IsValid ? world.DataCenter.Value.Name.ToString() : string.Empty;
        return new HomeWorld(PlayerState.HomeWorld.RowId, world.Name.ToString(), dataCenter);
    }
}
