using System.Net;
using System.Text.Json;

namespace TomestonePhone.Networking;

public sealed class GiphyClient : IDisposable
{
    private readonly HttpClient httpClient = new()
    {
        BaseAddress = new Uri("https://api.giphy.com/", UriKind.Absolute),
    };

    public async Task<IReadOnlyList<GiphyGifResult>> SearchAsync(string apiKey, string query, string rating, int limit, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var path = $"v1/gifs/search?api_key={Uri.EscapeDataString(apiKey)}&q={Uri.EscapeDataString(query)}&limit={limit}&offset=0&rating={Uri.EscapeDataString(rating)}&bundle=messaging_non_clips";
        return await this.GetResultsAsync(path, cancellationToken);
    }

    public async Task<IReadOnlyList<GiphyGifResult>> GetTrendingAsync(string apiKey, string rating, int limit, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return [];
        }

        var path = $"v1/gifs/trending?api_key={Uri.EscapeDataString(apiKey)}&limit={limit}&rating={Uri.EscapeDataString(rating)}&bundle=messaging_non_clips";
        return await this.GetResultsAsync(path, cancellationToken);
    }

    public void Dispose()
    {
        this.httpClient.Dispose();
    }

    private async Task<IReadOnlyList<GiphyGifResult>> GetResultsAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await this.httpClient.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var results = new List<GiphyGifResult>();

        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return results;
        }

        foreach (var item in data.EnumerateArray())
        {
            var id = item.TryGetProperty("id", out var idProperty) ? idProperty.GetString() ?? string.Empty : string.Empty;
            var title = item.TryGetProperty("title", out var titleProperty) ? titleProperty.GetString() ?? "GIF" : "GIF";
            var pageUrl = item.TryGetProperty("url", out var pageProperty) ? pageProperty.GetString() ?? string.Empty : string.Empty;
            var gifUrl = TryGetImageUrl(item, "original")
                ?? TryGetImageUrl(item, "downsized")
                ?? string.Empty;
            var previewUrl = TryGetImageUrl(item, "fixed_width_small_still")
                ?? TryGetImageUrl(item, "fixed_width_still")
                ?? gifUrl;

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(gifUrl))
            {
                continue;
            }

            results.Add(new GiphyGifResult(id, WebUtility.HtmlDecode(title), gifUrl, previewUrl, pageUrl));
        }

        return results;
    }

    private static string? TryGetImageUrl(JsonElement item, string imageKey)
    {
        if (!item.TryGetProperty("images", out var images)
            || !images.TryGetProperty(imageKey, out var image)
            || !image.TryGetProperty("url", out var urlProperty))
        {
            return null;
        }

        return urlProperty.GetString();
    }
}
