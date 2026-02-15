using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using SuchByte.MacroDeck.ActionButton;
using SuchByte.MacroDeck.CottleIntegration;
using SuchByte.MacroDeck.GUI;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Icons;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Plugins;

namespace ImageFromUrlPlugin;

public sealed class SpotifyImageAction : PluginAction
{
    private const string IconPackName = "ImageFromUrl";
    private const string IconPackPackageId = "felba.ImageFromUrl";
    private const int MinButtonCooldownSeconds = 12;
    private const int ButtonCooldownJitterSeconds = 6;
    private const int TitleChangeDebounceSeconds = 5;
    private static readonly ConcurrentDictionary<string, DateTimeOffset> LastFetchByKey = new();
    private static readonly ConcurrentDictionary<string, DateTimeOffset> LastFetchByButton = new();
    private static readonly ConcurrentDictionary<string, string> LastTitleByButton = new();
    private static readonly ConcurrentDictionary<string, DateTimeOffset> NextAllowedByButton = new();
    private static readonly ConcurrentDictionary<string, (string Key, DateTimeOffset ChangedAt)> PendingTitleByButton = new();
    private static readonly string IconPackFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Macro Deck",
        "iconpacks",
        IconPackPackageId);
    private static readonly string IconPackManifestPath = Path.Combine(IconPackFolder, "ExtensionManifest.json");

    public override string Name => "spotify_image";
    public override string Description => "Finds the top Spotify track and sets the button icon to its album art.";
    public override bool CanConfigure => true;

    public SpotifyImageAction()
    {
        ConfigurationSummary = "Spotify Album Art (Title + Artist)";
    }

    public override ActionConfigControl GetActionConfigControl(ActionConfigurator actionConfigurator)
    {
        return new SpotifyImageActionConfigControl(this);
    }

    public override void Trigger(string clientId, ActionButton actionButton)
    {
        _ = UpdateButtonIconAsync(actionButton);
    }

    public override void OnActionButtonLoaded()
    {
        ConfigurationSummary = BuildSummary(GetConfig());
    }

    internal SpotifyImageActionConfig GetConfig()
    {
        if (string.IsNullOrWhiteSpace(Configuration))
        {
            return new SpotifyImageActionConfig();
        }

        try
        {
            return JsonSerializer.Deserialize<SpotifyImageActionConfig>(Configuration) ?? new SpotifyImageActionConfig();
        }
        catch
        {
            return new SpotifyImageActionConfig();
        }
    }

    internal void UpdateConfiguration(SpotifyImageActionConfig config)
    {
        Configuration = JsonSerializer.Serialize(config);
        ConfigurationSummary = BuildSummary(config);
    }

    private static string BuildSummary(SpotifyImageActionConfig config)
    {
        var title = config.Title?.Trim() ?? string.Empty;
        var artist = config.Artist?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(artist))
        {
            return "No track set";
        }

        if (string.IsNullOrEmpty(title))
        {
            return artist;
        }

        if (string.IsNullOrEmpty(artist))
        {
            return title;
        }

        return $"{title} - {artist}";
    }

    private async Task UpdateButtonIconAsync(ActionButton actionButton)
    {
        try
        {
            var config = GetConfig();
            var resolvedTitle = TemplateManager.RenderTemplate(config.Title ?? string.Empty).Trim();
            var resolvedArtist = TemplateManager.RenderTemplate(config.Artist ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(resolvedTitle) || string.IsNullOrWhiteSpace(resolvedArtist))
            {
                MacroDeckLogger.Warning(ImageFromUrlPlugin.Instance, "Title or artist is missing. Configure the action first.");
                return;
            }

            var iconId = ImageProcessing.CreateDeterministicIconId(resolvedTitle, resolvedArtist);
            var iconString = $"{IconPackName}.{iconId}";
            var key = $"{resolvedTitle}|{resolvedArtist}";

            if (ShouldDebounceTitleChange(actionButton.Guid, key))
            {
                MacroDeckLogger.Info(ImageFromUrlPlugin.Instance, "Waiting for title/artist to settle before fetching Spotify art.");
                return;
            }

            var iconPack = IconManager.GetIconPackByName(IconPackName);
            if (iconPack == null)
            {
                IconManager.CreateIconPack(IconPackName, "felba", "1.0.0");
                iconPack = IconManager.GetIconPackByName(IconPackName);
            }

            if (iconPack == null)
            {
                MacroDeckLogger.Error(ImageFromUrlPlugin.Instance, "Failed to create or load icon pack.");
                return;
            }

            var existing = IconManager.GetIcon(iconPack, iconId);
            if (LastTitleByButton.TryGetValue(actionButton.Guid, out var lastTitle) &&
                string.Equals(lastTitle, resolvedTitle, StringComparison.OrdinalIgnoreCase) &&
                existing != null)
            {
                ApplyIcon(actionButton, iconString);
                return;
            }

            if (existing != null)
            {
                if (string.Equals(actionButton.IconOff, iconString, StringComparison.OrdinalIgnoreCase) &&
                    !IsRefreshDue(key, config.MinRefreshSeconds))
                {
                    ApplyIcon(actionButton, iconString);
                    LastTitleByButton[actionButton.Guid] = resolvedTitle;
                    return;
                }

                if (config.OnlyUpdateIfMissing)
                {
                    ApplyIcon(actionButton, iconString);
                    LastTitleByButton[actionButton.Guid] = resolvedTitle;
                    return;
                }

                if (!IsRefreshDue(key, config.MinRefreshSeconds))
                {
                    ApplyIcon(actionButton, iconString);
                    LastTitleByButton[actionButton.Guid] = resolvedTitle;
                    return;
                }
            }
            else if (!IsRefreshDue(key, config.MinRefreshSeconds))
            {
                MacroDeckLogger.Info(ImageFromUrlPlugin.Instance, "Skipping Spotify fetch due to refresh cooldown.");
                return;
            }

            if (!IsButtonCooldownDue(actionButton.Guid))
            {
                MacroDeckLogger.Info(ImageFromUrlPlugin.Instance, "Skipping Spotify fetch due to button cooldown.");
                return;
            }

            LastFetchByButton[actionButton.Guid] = DateTimeOffset.UtcNow;
            NextAllowedByButton[actionButton.Guid] = DateTimeOffset.UtcNow.AddSeconds(
                MinButtonCooldownSeconds + Random.Shared.Next(0, ButtonCooldownJitterSeconds + 1));

            var imageUrl = await SpotifyClient.SearchAlbumArtUrlAsync(resolvedTitle, resolvedArtist, ImageFromUrlPlugin.Instance);
            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                MacroDeckLogger.Warning(ImageFromUrlPlugin.Instance, "Spotify search returned no album art.");
                return;
            }

            using var original = await SpotifyClient.DownloadImageAsync(imageUrl);
            using var resized = ImageProcessing.ToSquareImage(original, 128);

            if (existing == null)
            {
                IconManager.AddIconImage(iconPack, resized, iconId);
                IconManager.SaveIconPack(iconPack);
                TryRefreshIconPack(ImageFromUrlPlugin.Instance);
            }

            ApplyIcon(actionButton, iconString);
            LastFetchByKey[key] = DateTimeOffset.UtcNow;
            LastTitleByButton[actionButton.Guid] = resolvedTitle;
        }
        catch (Exception ex)
        {
            MacroDeckLogger.Error(ImageFromUrlPlugin.Instance, typeof(SpotifyImageAction), ex.Message);
        }
    }

    private static bool IsRefreshDue(string key, int minSeconds)
    {
        if (minSeconds <= 0)
        {
            return true;
        }

        if (!LastFetchByKey.TryGetValue(key, out var last))
        {
            return true;
        }

        return DateTimeOffset.UtcNow - last >= TimeSpan.FromSeconds(minSeconds);
    }

    private static bool IsButtonCooldownDue(string buttonGuid)
    {
        if (NextAllowedByButton.TryGetValue(buttonGuid, out var nextAllowed))
        {
            return DateTimeOffset.UtcNow >= nextAllowed;
        }

        if (!LastFetchByButton.TryGetValue(buttonGuid, out var last))
        {
            return true;
        }

        return DateTimeOffset.UtcNow - last >= TimeSpan.FromSeconds(MinButtonCooldownSeconds);
    }

    private static bool ShouldDebounceTitleChange(string buttonGuid, string key)
    {
        if (!PendingTitleByButton.TryGetValue(buttonGuid, out var pending) ||
            !string.Equals(pending.Key, key, StringComparison.Ordinal))
        {
            PendingTitleByButton[buttonGuid] = (key, DateTimeOffset.UtcNow);
            return true;
        }

        return DateTimeOffset.UtcNow - pending.ChangedAt < TimeSpan.FromSeconds(TitleChangeDebounceSeconds);
    }

    private static void ApplyIcon(ActionButton actionButton, string iconString)
    {
        actionButton.IconOff = iconString;
        if (string.IsNullOrWhiteSpace(actionButton.IconOn))
        {
            actionButton.IconOn = iconString;
        }

        actionButton.UpdateBindingState();
    }

    private static void TryRefreshIconPack(ImageFromUrlPlugin plugin)
    {
        try
        {
            if (!File.Exists(IconPackManifestPath))
            {
                return;
            }

            var json = File.ReadAllText(IconPackManifestPath);
            if (JsonNode.Parse(json) is not JsonObject obj)
            {
                return;
            }

            var versionSuffix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            obj["version"] = $"1.0.0+{versionSuffix}";
            File.WriteAllText(IconPackManifestPath, obj.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true
            }));

            IconManager.LoadIconPack(IconPackFolder);
        }
        catch (Exception ex)
        {
            MacroDeckLogger.Warning(plugin, $"Icon pack refresh failed: {ex.Message}");
        }
    }
}
