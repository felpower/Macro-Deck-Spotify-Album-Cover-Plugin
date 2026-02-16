using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Plugins;

namespace SpotifyAlbumCoverPlugin;

public static class SpotifyClient
{
    private static readonly HttpClient HttpClient = new();
    private static readonly SemaphoreSlim TokenLock = new(1, 1);
    private static readonly SemaphoreSlim RequestLock = new(1, 1);
    private static readonly object GlobalGate = new();
    private static string? _accessToken;
    private static DateTimeOffset _accessTokenExpiresAt = DateTimeOffset.MinValue;
    private static DateTimeOffset _nextRequestAllowedAt = DateTimeOffset.MinValue;
    private static DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;
    private static DateTimeOffset _lastSkipLogAt = DateTimeOffset.MinValue;
    private static readonly ConcurrentDictionary<string, (string Url, DateTimeOffset ExpiresAt)> AlbumArtCache = new();
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan GlobalMinInterval = TimeSpan.FromSeconds(15);

    public static async Task<string?> SearchAlbumArtUrlAsync(string title, string artist, MacroDeckPlugin plugin)
    {
        var cacheKey = BuildCacheKey(title, artist);
        if (TryGetCachedUrl(cacheKey, out var cachedUrl))
        {
            return cachedUrl;
        }

        var clientId = PluginConfiguration.GetValue(plugin, "SpotifyClientId");
        var clientSecret = PluginConfiguration.GetValue(plugin, "SpotifyClientSecret");

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            MacroDeckLogger.Warning(plugin, "Spotify Client ID/Secret are not configured.");
            return null;
        }

        if (!TryBeginGlobalRequest(plugin, out var waitFor))
        {
            return null;
        }

        var token = await GetAccessTokenAsync(clientId, clientSecret, plugin);
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var query = $"track:{title} artist:{artist}";
        using var response = await SendWithGlobalRateLimitAsync(() =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.spotify.com/v1/search?q={Uri.EscapeDataString(query)}&type=track&limit=1");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return request;
        }, plugin);
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

        var url = urlProp.GetString();
        if (!string.IsNullOrWhiteSpace(url))
        {
            AlbumArtCache[cacheKey] = (url, DateTimeOffset.UtcNow.Add(CacheDuration));
        }

        return url;
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
            _ = TokenLock.Release();
        }
    }

    private static async Task<HttpResponseMessage> SendWithGlobalRateLimitAsync(Func<HttpRequestMessage> requestFactory, MacroDeckPlugin plugin)
    {
        await RequestLock.WaitAsync();
        try
        {
            await WaitForGlobalRateLimitAsync();
            using var request = requestFactory();
            _lastRequestAt = DateTimeOffset.UtcNow;
            var response = await HttpClient.SendAsync(request);
            if (response.StatusCode != (HttpStatusCode)429)
            {
                return response;
            }

            var delay = GetRetryDelay(response);
            _nextRequestAllowedAt = DateTimeOffset.UtcNow.Add(delay);
            MacroDeckLogger.Warning(plugin, $"Spotify rate limit hit. Backing off for {delay.TotalSeconds:0} seconds.");

            response.Dispose();
            await WaitForGlobalRateLimitAsync();
            using var retryRequest = requestFactory();
            return await HttpClient.SendAsync(retryRequest);
        }
        finally
        {
            _ = RequestLock.Release();
        }
    }

    private static async Task WaitForGlobalRateLimitAsync()
    {
        var now = DateTimeOffset.UtcNow;
        if (now >= _nextRequestAllowedAt)
        {
            return;
        }

        var delay = _nextRequestAllowedAt - now;
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay);
        }
    }

    private static TimeSpan GetRetryDelay(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter?.Delta;
        if (retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero)
        {
            return retryAfter.Value + TimeSpan.FromMilliseconds(Random.Shared.Next(250, 1250));
        }

        return DefaultRetryDelay + TimeSpan.FromMilliseconds(Random.Shared.Next(250, 1250));
    }

    private static string BuildCacheKey(string title, string artist)
    {
        return $"{title}".Trim().ToLowerInvariant() + "|" + $"{artist}".Trim().ToLowerInvariant();
    }

    private static bool TryBeginGlobalRequest(MacroDeckPlugin plugin, out TimeSpan waitFor)
    {
        lock (GlobalGate)
        {
            var now = DateTimeOffset.UtcNow;
            var minIntervalAt = _lastRequestAt + GlobalMinInterval;
            var allowedAt = _nextRequestAllowedAt > minIntervalAt ? _nextRequestAllowedAt : minIntervalAt;
            if (now < allowedAt)
            {
                waitFor = allowedAt - now;
                if (now - _lastSkipLogAt > TimeSpan.FromSeconds(10))
                {
                    MacroDeckLogger.Info(plugin, $"Skipping Spotify request due to global cooldown. Next in {waitFor.TotalSeconds:0}s.");
                    _lastSkipLogAt = now;
                }
                return false;
            }

            _lastRequestAt = now;
            waitFor = TimeSpan.Zero;
            return true;
        }
    }

    private static bool TryGetCachedUrl(string cacheKey, out string? url)
    {
        url = null;
        if (!AlbumArtCache.TryGetValue(cacheKey, out var entry))
        {
            return false;
        }

        if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _ = AlbumArtCache.TryRemove(cacheKey, out _);
            return false;
        }

        url = entry.Url;
        return !string.IsNullOrWhiteSpace(url);
    }
}


