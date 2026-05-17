using System.Net.Http;
using System.Text.Json;

namespace AuxMarketboard.Services;

public sealed class UniversalisClient : IDisposable
{
    private static readonly Uri BaseUri = new("https://universalis.app/api/v2/");
    private static readonly TimeSpan MinGapBetweenRequests = TimeSpan.FromMilliseconds(120);
    private readonly HttpClient httpClient;
    private DateTime lastRequestUtc = DateTime.MinValue;
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public UniversalisClient()
    {
        httpClient = new HttpClient
        {
            BaseAddress = BaseUri,
            Timeout = TimeSpan.FromSeconds(20),
        };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AuxMarketboard/0.1");
    }

    public async Task<Dictionary<uint, UniversalisItemResponse>> GetCurrentMarketDataAsync(
        string worldOrDc,
        IReadOnlyCollection<uint> itemIds,
        int requiredPrices,
        bool includeNq,
        bool includeHq,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<uint, UniversalisItemResponse>();
        if (itemIds.Count == 0)
        {
            return result;
        }

        foreach (var chunk in itemIds.Chunk(100))
        {
            var itemCsv = string.Join(",", chunk);
            var endpoint = BuildEndpoint(worldOrDc, itemCsv, requiredPrices, includeNq, includeHq);
            using var response = await GetWithRetryAsync(endpoint, cancellationToken).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            MergeParsedResponse(json, result);
        }

        return result;
    }

    private string BuildEndpoint(string worldOrDc, string itemCsv, int requiredPrices, bool includeNq, bool includeHq)
    {
        var query = $"listings={requiredPrices}&entries={requiredPrices}";
        if (includeHq && !includeNq)
        {
            query += "&hq=true";
        }
        else if (!includeHq && includeNq)
        {
            query += "&hq=false";
        }

        return $"{Uri.EscapeDataString(worldOrDc)}/{itemCsv}?{query}";
    }

    private async Task<HttpResponseMessage> GetWithRetryAsync(string endpoint, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var now = DateTime.UtcNow;
            var wait = MinGapBetweenRequests - (now - lastRequestUtc);
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            }

            var response = await httpClient.GetAsync(endpoint, cancellationToken).ConfigureAwait(false);
            lastRequestUtc = DateTime.UtcNow;

            if ((int)response.StatusCode == 429 && attempt < 3)
            {
                var retryDelay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(attempt);
                response.Dispose();
                await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            response.EnsureSuccessStatusCode();
            return response;
        }

        throw new InvalidOperationException("Universalis request failed repeatedly due to rate limiting.");
    }

    private void MergeParsedResponse(string json, Dictionary<uint, UniversalisItemResponse> result)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("items", out _))
        {
            var multi = JsonSerializer.Deserialize<UniversalisMultiResponse>(json, jsonOptions);
            if (multi?.Items is null)
            {
                return;
            }

            foreach (var pair in multi.Items)
            {
                if (uint.TryParse(pair.Key, out var itemId))
                {
                    result[itemId] = pair.Value;
                }
            }

            return;
        }

        if (root.TryGetProperty("itemID", out var itemIdElement))
        {
            var single = JsonSerializer.Deserialize<UniversalisItemResponse>(json, jsonOptions);
            if (single is null)
            {
                return;
            }

            if (itemIdElement.TryGetUInt32(out var singleId))
            {
                result[singleId] = single;
            }
        }
    }

    public void Dispose()
    {
        httpClient.Dispose();
    }
}
