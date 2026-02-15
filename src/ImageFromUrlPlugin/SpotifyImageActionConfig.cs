namespace ImageFromUrlPlugin;

public sealed class SpotifyImageActionConfig
{
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public int MinRefreshSeconds { get; set; } = 300;
    public bool OnlyUpdateIfMissing { get; set; } = true;
}
