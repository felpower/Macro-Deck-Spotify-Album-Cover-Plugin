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

namespace SpotifyAlbumCoverPlugin;

public sealed class SpotifyImageAction : PluginAction
{
    private const string IconPackName = "SpotifyAlbumCover";
    private const string IconPackPackageId = "felba.SpotifyAlbumCoverPlugin";
    private const int MinButtonCooldownSeconds = 12;
    private const int ButtonCooldownJitterSeconds = 6;
    private const int TitleChangeDebounceSeconds = 5;
    private static readonly ConcurrentDictionary<string, DateTimeOffset> LastFetchByKey = new();
    private static readonly ConcurrentDictionary<string, DateTimeOffset> LastFetchByButton = new();
    private static readonly ConcurrentDictionary<string, string> LastTitleByButton = new();
    private static readonly ConcurrentDictionary<string, DateTimeOffset> NextAllowedByButton = new();
    private static readonly ConcurrentDictionary<string, (string Key, DateTimeOffset ChangedAt)> PendingTitleByButton = new();
    private static readonly ConcurrentDictionary<string, string> LastIconIdByButton = new();
    private static readonly ConcurrentDictionary<string, int> IconUsageCounts = new();
    private static readonly ConcurrentDictionary<string, CancellationTokenSource> DebounceTimers = new();
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

    public static void TriggerUpdateForAllButtons()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                // Wir nutzen das statische Deck-Objekt, um an die Buttons zu kommen
                // Das funktioniert in fast allen Macro Deck 2 Versionen
                foreach (var profile in SuchByte.MacroDeck.Profiles.ProfileManager.Profiles)
                {
                    // Da Folders.Buttons nicht geht, versuchen wir es über die flache Liste, 
                    // falls vorhanden, oder wir nutzen die statische Instanz
                    var buttons = profile.Folders.SelectMany(f => f.ActionButtons);

                    foreach (var actionButton in buttons)
                    {
                        if (actionButton.Actions.Any(a => a is SpotifyImageAction || a != null && a.Name == "spotify_image"))
                        {
                            var actionInstance = new SpotifyImageAction();
                            await actionInstance.UpdateButtonIconAsync(actionButton);
                        }
                    }
                }

                var iconPack = IconManager.GetIconPackByName(IconPackName);
                if (iconPack != null)
                {
                    var activeIds = GetActiveIconIds();
                    CleanupUnusedIcons(iconPack, activeIds);
                }
            }
            catch (Exception ex)
            {
                // Wenn alles oben fehlschlägt, nutzen wir einen Trace, um den Build nicht zu stoppen
                MacroDeckLogger.Trace(SpotifyAlbumCoverPlugin.Instance, "Update trigger silent fail: " + ex.Message);
            }
        });
    }

    private static HashSet<string> GetActiveIconIds()
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var profile in SuchByte.MacroDeck.Profiles.ProfileManager.Profiles)
            {
                var buttons = profile.Folders.SelectMany(f => f.ActionButtons);
                foreach (var actionButton in buttons)
                {
                    if (actionButton.Actions.Any(a => a is SpotifyImageAction || a != null && a.Name == "spotify_image"))
                    {
                        ids.Add(ImageProcessing.CreateDeterministicIconIdFromString($"button:{actionButton.Guid}"));
                    }
                }
            }
        }
        catch
        {
            return ids;
        }

        return ids;
    }

    private static void CleanupUnusedIcons(IconPack iconPack, HashSet<string> activeIds)
    {
        if (activeIds.Count == 0)
        {
            return;
        }

        try
        {
            var icons = new List<dynamic>();
            foreach (var icon in ((dynamic)iconPack).Icons)
            {
                icons.Add(icon);
            }

            var deleted = false;
            foreach (var icon in icons)
            {
                var iconId = (string)icon.Id;
                if (!activeIds.Contains(iconId))
                {
                    IconManager.DeleteIcon(iconPack, icon);
                    deleted = true;
                }
            }

            if (deleted)
            {
                IconManager.SaveIconPack(iconPack);
                TryRefreshIconPack(SpotifyAlbumCoverPlugin.Instance);
            }
        }
        catch (Exception ex)
        {
            MacroDeckLogger.Warning(SpotifyAlbumCoverPlugin.Instance, $"Icon cleanup skipped: {ex.Message}");
        }
    }

    public override void OnActionButtonLoaded()
    {
        ConfigurationSummary = BuildSummary(GetConfig());
    }

    internal SpotifyImageActionConfig GetConfig()
    {
        if (string.IsNullOrWhiteSpace(Configuration))
        {
            return ApplyDefaultVariables(new SpotifyImageActionConfig());
        }

        try
        {
            var config = JsonSerializer.Deserialize<SpotifyImageActionConfig>(Configuration) ?? new SpotifyImageActionConfig();
            return ApplyDefaultVariables(config);
        }
        catch
        {
            return ApplyDefaultVariables(new SpotifyImageActionConfig());
        }
    }

    private static SpotifyImageActionConfig ApplyDefaultVariables(SpotifyImageActionConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Title))
        {
            config.Title = "{current_playing_title}";
        }

        if (string.IsNullOrWhiteSpace(config.Artist))
        {
            config.Artist = "{current_playing_artist}";
        }

        return config;
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

    internal async Task UpdateButtonIconAsync(ActionButton actionButton)
    {
        try
        {
            var config = GetConfig();
            var resolvedTitle = TemplateManager.RenderTemplate(config.Title ?? string.Empty).Trim();
            var resolvedArtist = TemplateManager.RenderTemplate(config.Artist ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(resolvedTitle) || string.IsNullOrWhiteSpace(resolvedArtist))
            {
                MacroDeckLogger.Warning(SpotifyAlbumCoverPlugin.Instance, "Title or artist is missing. Configure the action first.");
                return;
            }

            // Wir nutzen Ticks nur für den Button-Sync, um das Tablet-Caching zu umgehen
            var key = $"{resolvedTitle}|{resolvedArtist}";
            var iconIdBase = ImageProcessing.CreateDeterministicIconIdFromString($"button:{actionButton.Guid}");

            var iconPack = IconManager.GetIconPackByName(IconPackName);
            if (iconPack == null)
            {
                IconManager.CreateIconPack(IconPackName, "felba", "1.0.0");
                iconPack = IconManager.GetIconPackByName(IconPackName);
            }

            if (iconPack == null)
            {
                MacroDeckLogger.Error(SpotifyAlbumCoverPlugin.Instance, "Failed to create or load icon pack.");
                return;
            }

            // Debounce-Check
            if (ShouldDebounceTitleChange(actionButton.Guid, key, out var keyChanged))
            {
                if (keyChanged)
                {
                    ReleaseIconForButton(actionButton.Guid, iconPack);
                    ClearIcon(actionButton);
                    ScheduleDebouncedUpdate(actionButton, key);
                }
                MacroDeckLogger.Info(SpotifyAlbumCoverPlugin.Instance, "Waiting for title/artist to settle before fetching Spotify art.");
                return;
            }

            // Prüfen, ob wir dieses Lied schon im Speicher/Pack haben
            var existing = IconManager.GetIcon(iconPack, iconIdBase);

            // Falls das Icon noch nicht existiert oder ein Refresh fällig ist
            if (existing == null || IsRefreshDue(key, config.MinRefreshSeconds))
            {
                if (!IsButtonCooldownDue(actionButton.Guid))
                {
                    MacroDeckLogger.Info(SpotifyAlbumCoverPlugin.Instance, "Skipping Spotify fetch due to button cooldown.");
                    return;
                }

                LastFetchByButton[actionButton.Guid] = DateTimeOffset.UtcNow;
                NextAllowedByButton[actionButton.Guid] = DateTimeOffset.UtcNow.AddSeconds(
                    MinButtonCooldownSeconds + Random.Shared.Next(0, ButtonCooldownJitterSeconds + 1));

                var imageUrl = await SpotifyClient.SearchAlbumArtUrlAsync(resolvedTitle, resolvedArtist, SpotifyAlbumCoverPlugin.Instance);

                if (!string.IsNullOrWhiteSpace(imageUrl))
                {
                    using var original = await SpotifyClient.DownloadImageAsync(imageUrl);
                    using var resized = ImageProcessing.ToSquareImage(original, 128);

                    if (existing != null)
                    {
                        IconManager.DeleteIcon(iconPack, existing);
                    }

                    IconManager.AddIconImage(iconPack, resized, iconIdBase);
                    IconManager.SaveIconPack(iconPack);

                    await Task.Delay(300);

                    IconManager.LoadIconPack(IconPackFolder);
                    TryRefreshIconPack(SpotifyAlbumCoverPlugin.Instance);
                }
            }

            var iconString = $"{IconPackName}.{iconIdBase}";

            ApplyIconAndTrack(actionButton, iconPack, iconIdBase, iconString);

            LastFetchByKey[key] = DateTimeOffset.UtcNow;
            LastTitleByButton[actionButton.Guid] = resolvedTitle;
        }
        catch (Exception ex)
        {
            MacroDeckLogger.Error(SpotifyAlbumCoverPlugin.Instance, typeof(SpotifyImageAction), "Error in UpdateButtonIconAsync: " + ex.Message);
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

    private static bool ShouldDebounceTitleChange(string buttonGuid, string key, out bool keyChanged)
    {
        keyChanged = false;
        if (!PendingTitleByButton.TryGetValue(buttonGuid, out var pending) ||
            !string.Equals(pending.Key, key, StringComparison.Ordinal))
        {
            PendingTitleByButton[buttonGuid] = (key, DateTimeOffset.UtcNow);
            keyChanged = true;
            return true;
        }

        return DateTimeOffset.UtcNow - pending.ChangedAt < TimeSpan.FromSeconds(TitleChangeDebounceSeconds);
    }

    private void ScheduleDebouncedUpdate(ActionButton actionButton, string key)
    {
        var cts = new CancellationTokenSource();
        if (DebounceTimers.TryGetValue(actionButton.Guid, out var existingCts))
        {
            existingCts.Cancel();
            existingCts.Dispose();
        }

        DebounceTimers[actionButton.Guid] = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(TitleChangeDebounceSeconds), cts.Token);
                if (cts.IsCancellationRequested || actionButton.IsDisposed)
                {
                    return;
                }

                if (PendingTitleByButton.TryGetValue(actionButton.Guid, out var pending) &&
                    string.Equals(pending.Key, key, StringComparison.Ordinal))
                {
                    await UpdateButtonIconAsync(actionButton);
                }
            }
            catch (TaskCanceledException)
            {
            }
        });
    }

    private static void ApplyIconAndTrack(ActionButton actionButton, IconPack iconPack, string iconId, string iconString)
    {
        UpdateIconUsage(actionButton.Guid, iconPack, iconId);
        ApplyIcon(actionButton, iconString);
    }

    private static void UpdateIconUsage(string buttonGuid, IconPack iconPack, string iconId)
    {
        if (LastIconIdByButton.TryGetValue(buttonGuid, out var currentId) &&
            string.Equals(currentId, iconId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(currentId))
        {
            DecrementIconUsage(iconPack, currentId);
        }

        LastIconIdByButton[buttonGuid] = iconId;
        _ = IconUsageCounts.AddOrUpdate(iconId, 1, (_, count) => count + 1);
    }

    private static void ReleaseIconForButton(string buttonGuid, IconPack iconPack)
    {
        if (!LastIconIdByButton.TryRemove(buttonGuid, out var currentId))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(currentId))
        {
            DecrementIconUsage(iconPack, currentId);
        }
    }

    private static void DecrementIconUsage(IconPack iconPack, string iconId)
    {
        while (true)
        {
            if (!IconUsageCounts.TryGetValue(iconId, out var count))
            {
                return;
            }

            if (count <= 1)
            {
                if (IconUsageCounts.TryRemove(iconId, out _))
                {
                    DeleteIconIfExists(iconPack, iconId);
                }
                return;
            }

            if (IconUsageCounts.TryUpdate(iconId, count - 1, count))
            {
                return;
            }
        }
    }

    private static void DeleteIconIfExists(IconPack iconPack, string iconId)
    {
        var icon = IconManager.GetIcon(iconPack, iconId);
        if (icon == null)
        {
            return;
        }

        IconManager.DeleteIcon(iconPack, icon);
        IconManager.SaveIconPack(iconPack);
        TryRefreshIconPack(SpotifyAlbumCoverPlugin.Instance);
    }

private static void ApplyIcon(ActionButton actionButton, string iconString)
{
    actionButton.IconOff = iconString;
    actionButton.IconOn = iconString;
    actionButton.UpdateBindingState();

    // Trigger für das Tablet
    SuchByte.MacroDeck.Variables.VariableManager.SetValue(
        "spotify_sync_trigger", 
        DateTime.Now.Ticks.ToString(), 
        SuchByte.MacroDeck.Variables.VariableType.String, 
        SpotifyAlbumCoverPlugin.Instance, 
        [string.Empty]
    );
}

    private static void ClearIcon(ActionButton actionButton)
    {
        actionButton.IconOff = string.Empty;
        actionButton.IconOn = string.Empty;
        actionButton.UpdateBindingState();
    }

    private static void TryRefreshIconPack(SpotifyAlbumCoverPlugin plugin)
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

            _ = IconManager.LoadIconPack(IconPackFolder);
        }
        catch (Exception ex)
        {
            MacroDeckLogger.Warning(plugin, $"Icon pack refresh failed: {ex.Message}");
        }
    }
}


