using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using AuxMarketboard.Services;
using AuxMarketboard.UI;
using AuxUi;

namespace AuxMarketboard;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/mmb";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private readonly WindowSystem windowSystem = new("AuxMarketboard");
    private readonly MainWindow mainWindow;
    private readonly MarketService marketService;
    private readonly UniversalisClient universalisClient;

    public Configuration Configuration { get; }

    public Plugin()
    {
        AuxFonts.Initialize(PluginInterface);

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(PluginInterface);
        if (Configuration.Migrate())
        {
            Configuration.Save();
        }

        var version = typeof(Plugin).Assembly.GetName().Version?.ToString(3) ?? "0.2.0";
        universalisClient = new UniversalisClient(version);
        var itemResolver = new ItemResolver(DataManager);
        itemResolver.Warm();
        var importer = new ListImporter(itemResolver);

        marketService = new MarketService(
            Configuration,
            universalisClient,
            new WorldDirectory(),
            ReadHomeWorld,
            (message, exception) => Log.Error(exception, message));
        marketService.StartDirectoryLoad();

        mainWindow = new MainWindow(marketService, itemResolver, importer);
        windowSystem.AddWindow(mainWindow);

        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open AuxMarketboard",
        });
    }

    public void Dispose()
    {
        CommandManager.RemoveHandler(CommandName);
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        windowSystem.RemoveAllWindows();
        mainWindow.Dispose();
        marketService.Dispose();
        universalisClient.Dispose();
        AuxFonts.Dispose();
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

    private void OnCommand(string command, string args)
    {
        ToggleMainUi();
    }

    private void ToggleMainUi()
    {
        mainWindow.Toggle();
    }
}
