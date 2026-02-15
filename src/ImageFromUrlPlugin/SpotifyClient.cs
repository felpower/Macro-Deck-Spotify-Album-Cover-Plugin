using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Plugins;

namespace ImageFromUrlPlugin;

public static class SpotifyClient
{
    private static readonly HttpClient HttpClient = new();
    private static readonly SemaphoreSlim TokenLock = new(1, 1);
    private static string? _accessToken;
    private static DateTimeOffset _accessTokenExpiresAt = DateTimeOffset.MinValue;

    public static async Task<string?> SearchAlbumArtUrlAsync(string title, string artist, MacroDeckPlugin plugin)
    {
        var clientId = PluginConfiguration.GetValue(plugin, "SpotifyClientId");
        var clientSecret = PluginConfiguration.GetValue(plugin, "SpotifyClientSecret");

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            MacroDeckLogger.Warning(plugin, "Spotify Client ID/Secret are not configured.");
            return null;
        }

        var token = await GetAccessTokenAsync(clientId, clientSecret, plugin);
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var query = $"track:{title} artist:{artist}";
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.spotify.com/v1/search?q={Uri.EscapeDataString(query)}&type=track&limit=1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await HttpClient.SendAsync(request);
        if (response.StatusCode == (HttpStatusCode)429)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta?.TotalSeconds;
            if (retryAfter.HasValue)
            {
                MacroDeckLogger.Warning(plugin, $"Spotify rate limit hit. Retry after {retryAfter.Value:0} seconds.");
            }
            else
            {
                MacroDeckLogger.Warning(plugin, "Spotify rate limit hit.");
            }

            return null;
        }
        if (!response.IsSuccessStatusCode)
        {
            MacroDeckLogger.Warning(plugin, $"Spotify search failed: {(int)response.StatusCode} {response.ReasonPhrase}");
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("tracks", out var tracks)) return null;
        if (!tracks.TryGetProperty("items", out var items)) return null;
        if (items.GetArrayLength() == 0) return null;

        var first = items[0];
        if (!first.TryGetProperty("album", out var album)) return null;
        if (!album.TryGetProperty("images", out var images)) return null;
        if (images.GetArrayLength() == 0) return null;

        var image = images[0];
        if (!image.TryGetProperty("url", out var urlProp)) return null;

        return urlProp.GetString();
    }

    public static async Task<System.Drawing.Image> DownloadImageAsync(string url)
    {
        var bytes = await HttpClient.GetByteArrayAsync(url);
        using var stream = new MemoryStream(bytes);
        using var image = System.Drawing.Image.FromStream(stream);
        return (System.Drawing.Image)image.Clone();
    }

    private static async Task<string?> GetAccessTokenAsync(string clientId, string clientSecret, MacroDeckPlugin plugin)
    {
        if (DateTimeOffset.UtcNow < _accessTokenExpiresAt && !string.IsNullOrWhiteSpace(_accessToken))
        {
            return _accessToken;
        }

        await TokenLock.WaitAsync();
        try
        {
            if (DateTimeOffset.UtcNow < _accessTokenExpiresAt && !string.IsNullOrWhiteSpace(_accessToken))
            {
                return _accessToken;
            }

            var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token");
            var basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
            tokenRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);
            tokenRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });

            using var response = await HttpClient.SendAsync(tokenRequest);
            if (!response.IsSuccessStatusCode)
            {
                MacroDeckLogger.Warning(plugin, $"Spotify token request failed: {(int)response.StatusCode} {response.ReasonPhrase}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("access_token", out var tokenProp)) return null;
            if (!doc.RootElement.TryGetProperty("expires_in", out var expiresProp)) return null;

            _accessToken = tokenProp.GetString();
            var expiresIn = expiresProp.GetInt32();
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, expiresIn - 30));

            return _accessToken;
        }
        finally
        {
            TokenLock.Release();
        }
    }
}
