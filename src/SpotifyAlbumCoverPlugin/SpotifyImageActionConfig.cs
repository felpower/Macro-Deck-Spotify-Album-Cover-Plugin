namespace SpotifyAlbumCoverPlugin;

public sealed class SpotifyImageActionConfig
{
    public string Title { get; set; } = "{current_playing_title}";
    public string Artist { get; set; } = "{current_playing_artist}";
    public int MinRefreshSeconds { get; set; } = 300;
    public bool OnlyUpdateIfMissing { get; set; } = true;
}


