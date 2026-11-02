namespace AuxMarketboard.Services;

public readonly record struct WorldPlace(uint Id, string World, string DataCenter, string Region);

public sealed class WorldDirectory
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(30);

    private volatile Maps maps = Maps.Empty;
    private int loading;
    private long lastAttemptTicks;

    public bool IsLoaded => maps.ByName.Count > 0;

    public bool ShouldRetry => !IsLoaded
        && Volatile.Read(ref loading) == 0
        && DateTime.UtcNow.Ticks - Interlocked.Read(ref lastAttemptTicks) > RetryInterval.Ticks;

    public async Task LoadAsync(UniversalisClient client, CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref loading, 1) == 1)
        {
            return;
        }

        try
        {
            Interlocked.Exchange(ref lastAttemptTicks, DateTime.UtcNow.Ticks);
            var dataCenters = await client.GetDataCentersAsync(cancellationToken).ConfigureAwait(false);
            var worlds = await client.GetWorldsAsync(cancellationToken).ConfigureAwait(false);
            maps = Build(dataCenters, worlds);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
        }
        finally
        {
            Volatile.Write(ref loading, 0);
        }
    }

    public bool TryFind(string? worldName, uint worldId, out WorldPlace place)
    {
        var current = maps;
        if (worldId != 0 && current.ById.TryGetValue(worldId, out place))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(worldName) && current.ByName.TryGetValue(worldName.Trim(), out place))
        {
            return true;
        }

        place = default;
        return false;
    }

    public string DataCenterOf(string worldName)
        => maps.ByName.TryGetValue(worldName, out var place) ? place.DataCenter : string.Empty;

    public static Maps Build(IReadOnlyList<DataCenterInfo> dataCenters, IReadOnlyList<WorldInfo> worlds)
    {
        var namesById = worlds.GroupBy(x => x.Id).ToDictionary(x => x.Key, x => x.First().Name);
        var byName = new Dictionary<string, WorldPlace>(StringComparer.OrdinalIgnoreCase);
        var byId = new Dictionary<uint, WorldPlace>();
        foreach (var dataCenter in dataCenters)
        {
            foreach (var id in dataCenter.WorldIds)
            {
                if (!namesById.TryGetValue(id, out var name))
                {
                    continue;
                }

                var place = new WorldPlace(id, name, dataCenter.Name, dataCenter.Region);
                byName[name] = place;
                byId[id] = place;
            }
        }

        return new Maps(byName, byId);
    }

    public sealed record Maps(Dictionary<string, WorldPlace> ByName, Dictionary<uint, WorldPlace> ById)
    {
        public static readonly Maps Empty = new(new Dictionary<string, WorldPlace>(), new Dictionary<uint, WorldPlace>());
    }
}
