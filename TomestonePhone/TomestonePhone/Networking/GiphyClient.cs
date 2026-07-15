using System.Net;
using System.Text.Json;

namespace TomestonePhone.Networking;

public sealed class GiphyClient : IDisposable
{
    private readonly HttpClient httpClient = new()
    {
        BaseAddress = new Uri("https://api.klipy.com/", UriKind.Absolute),
    };

    public async Task<IReadOnlyList<GiphyGifResult>> SearchAsync(string apiKey, string query, string rating, int limit, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var path = $"v2/search?key={Uri.EscapeDataString(apiKey)}&q={Uri.EscapeDataString(query)}&limit={limit}&contentfilter={MapContentFilter(rating)}&media_filter=gif,tinygif,mediumgif,nanogif,preview";
        return await this.GetResultsAsync(path, cancellationToken);
    }

    public async Task<IReadOnlyList<GiphyGifResult>> GetTrendingAsync(string apiKey, string rating, int limit, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return [];
        }

        var path = $"v2/featured?key={Uri.EscapeDataString(apiKey)}&limit={limit}&contentfilter={MapContentFilter(rating)}&media_filter=gif,tinygif,mediumgif,nanogif,preview";
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

        if (!document.RootElement.TryGetProperty("results", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return results;
        }

        foreach (var item in data.EnumerateArray())
        {
            var id = item.TryGetProperty("id", out var idProperty) ? idProperty.GetString() ?? string.Empty : string.Empty;
            var title = item.TryGetProperty("content_description", out var descriptionProperty)
                ? descriptionProperty.GetString() ?? "GIF"
                : item.TryGetProperty("title", out var titleProperty) ? titleProperty.GetString() ?? "GIF" : "GIF";
            var pageUrl = item.TryGetProperty("itemurl", out var pageProperty) ? pageProperty.GetString() ?? string.Empty : string.Empty;
            var gifUrl = TryGetImageUrl(item, "gif")
                ?? TryGetImageUrl(item, "mediumgif")
                ?? string.Empty;
            var previewUrl = TryGetImageUrl(item, "tinygif")
                ?? TryGetImageUrl(item, "preview")
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
        if (!item.TryGetProperty("media_formats", out var images)
            || !images.TryGetProperty(imageKey, out var image)
            || !image.TryGetProperty("url", out var urlProperty))
        {
            return null;
        }

        return urlProperty.GetString();
    }

    private static string MapContentFilter(string rating)
    {
        return rating.Trim().ToLowerInvariant() switch
        {
            "g" => "high",
            "pg" => "medium",
            "pg-13" => "low",
            _ => "off",
        };
    }
}
