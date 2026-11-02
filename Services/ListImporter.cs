using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AuxMarketboard.Services;

public sealed record ImportResult(IReadOnlyList<ImportedItem> Items, int Skipped)
{
    public string Message
    {
        get
        {
            if (Items.Count == 0 && Skipped == 0)
            {
                return "Nothing to import";
            }

            var noun = Items.Count == 1 ? "item" : "items";
            return Skipped > 0
                ? $"Imported {Items.Count} {noun}, skipped {Skipped}"
                : $"Imported {Items.Count} {noun}";
        }
    }
}

public sealed class ListImporter
{
    private const string TeamcraftMarker = "/import/";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ItemResolver itemResolver;

    public ListImporter(ItemResolver itemResolver)
    {
        this.itemResolver = itemResolver;
    }

    public ImportResult Parse(string source)
    {
        var collector = new Collector();
        if (!string.IsNullOrWhiteSpace(source))
        {
            if (!TryParseArtisan(source, collector) && !TryParseTeamcraft(source, collector))
            {
                ParseLines(source, collector);
            }
        }

        return collector.ToResult();
    }

    private bool TryParseArtisan(string source, Collector collector)
    {
        ArtisanExport? export;
        try
        {
            export = JsonSerializer.Deserialize<ArtisanExport>(source, JsonOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        if (export is null || (export.Recipes is null && export.Items is null))
        {
            return false;
        }

        if (export.Recipes is not null)
        {
            foreach (var recipe in export.Recipes.Where(x => x.ID > 0 && x.Quantity > 0))
            {
                if (itemResolver.TryResolveRecipeToItem(recipe.ID, out var itemId, out _, out var yield))
                {
                    collector.Add(itemId, (long)recipe.Quantity * yield);
                }
                else
                {
                    collector.Skipped++;
                }
            }
        }

        if (export.Items is not null)
        {
            foreach (var group in export.Items.Where(x => x > 0).GroupBy(x => x))
            {
                if (itemResolver.TryGetName(group.Key, out _))
                {
                    collector.Add(group.Key, group.Count());
                }
                else
                {
                    collector.Skipped++;
                }
            }
        }

        return true;
    }

    private bool TryParseTeamcraft(string source, Collector collector)
    {
        if (!TryExtractTeamcraftPayload(source, out var payload))
        {
            return false;
        }

        foreach (var entry in payload.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = entry.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length < 3 || !uint.TryParse(parts[0], out var itemId) || !int.TryParse(parts[2], out var quantity) || quantity <= 0)
            {
                collector.Skipped++;
                continue;
            }

            if (itemResolver.TryGetName(itemId, out _))
            {
                collector.Add(itemId, quantity);
            }
            else
            {
                collector.Skipped++;
            }
        }

        return true;
    }

    private void ParseLines(string source, Collector collector)
    {
        foreach (var rawLine in source.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (TryParseLine(line, out var key, out var quantity) && itemResolver.TryResolveByText(key, out var itemId, out _))
            {
                collector.Add(itemId, quantity);
            }
            else
            {
                collector.Skipped++;
            }
        }
    }

    private static bool TryExtractTeamcraftPayload(string source, out string payload)
    {
        payload = string.Empty;
        var trimmed = source.Trim();
        if (!trimmed.Contains("ffxivteamcraft.com/import/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var index = uri.AbsolutePath.IndexOf(TeamcraftMarker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return false;
        }

        var encoded = uri.AbsolutePath[(index + TeamcraftMarker.Length)..].Trim('/');
        if (string.IsNullOrWhiteSpace(encoded))
        {
            return false;
        }

        payload = DecodeBase64ToUtf8(encoded);
        return !string.IsNullOrWhiteSpace(payload);
    }

    private static string DecodeBase64ToUtf8(string encoded)
    {
        try
        {
            var normalized = encoded.Replace('-', '+').Replace('_', '/');
            var pad = normalized.Length % 4;
            if (pad > 0)
            {
                normalized = normalized.PadRight(normalized.Length + (4 - pad), '=');
            }

            return Encoding.UTF8.GetString(Convert.FromBase64String(normalized));
        }
        catch (FormatException)
        {
            return string.Empty;
        }
    }

    private static bool TryParseLine(string line, out string itemKey, out int quantity)
    {
        itemKey = string.Empty;
        quantity = 1;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        line = StripBulletPrefix(line.Trim());

        var csv = line.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (csv.Length >= 2 && TryParsePositiveInt(csv[^1], out quantity))
        {
            itemKey = string.Join(",", csv.Take(csv.Length - 1)).Trim();
            quantity = Math.Max(1, quantity);
            return !string.IsNullOrWhiteSpace(itemKey);
        }

        var match = Regex.Match(line, @"^(?<item>.+?)\s*[xX×]\s*(?<qty>\d+)$");
        if (match.Success && TryParsePositiveInt(match.Groups["qty"].Value, out quantity))
        {
            itemKey = match.Groups["item"].Value.Trim();
            return !string.IsNullOrWhiteSpace(itemKey);
        }

        match = Regex.Match(line, @"^(?<qty>\d+)\s*[xX×]\s*(?<item>.+?)$");
        if (match.Success && TryParsePositiveInt(match.Groups["qty"].Value, out quantity))
        {
            itemKey = match.Groups["item"].Value.Trim();
            return !string.IsNullOrWhiteSpace(itemKey);
        }

        match = Regex.Match(line, @"^(?<item>.+?)\s*[:\-]\s*(?<qty>\d+)$");
        if (match.Success && TryParsePositiveInt(match.Groups["qty"].Value, out quantity))
        {
            itemKey = match.Groups["item"].Value.Trim();
            return !string.IsNullOrWhiteSpace(itemKey);
        }

        itemKey = line.Trim();
        quantity = 1;
        return !string.IsNullOrWhiteSpace(itemKey);
    }

    private static string StripBulletPrefix(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return string.Empty;
        }

        return Regex.Replace(line, @"^\s*(?:[-*•]\s+|\d+[.)]\s+)", string.Empty).Trim();
    }

    private static bool TryParsePositiveInt(string value, out int quantity)
    {
        quantity = 0;
        if (!long.TryParse(value.Trim(), out var parsed) || parsed <= 0)
        {
            return false;
        }

        quantity = parsed > int.MaxValue ? int.MaxValue : (int)parsed;
        return true;
    }

    private sealed class Collector
    {
        private readonly Dictionary<uint, long> quantities = new();
        private readonly List<uint> order = new();

        public int Skipped { get; set; }

        public void Add(uint itemId, long quantity)
        {
            if (!quantities.TryAdd(itemId, quantity))
            {
                quantities[itemId] += quantity;
            }
            else
            {
                order.Add(itemId);
            }
        }

        public ImportResult ToResult()
        {
            var items = order
                .Select(id => new ImportedItem(id, (int)Math.Clamp(quantities[id], 1, Configuration.MaxQuantity)))
                .ToList();
            return new ImportResult(items, Skipped);
        }
    }

    private sealed class ArtisanExport
    {
        public List<ArtisanRecipe>? Recipes { get; set; }
        public List<uint>? Items { get; set; }
    }

    private sealed class ArtisanRecipe
    {
        public uint ID { get; set; }
        public int Quantity { get; set; }
    }
}
