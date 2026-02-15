using System.Drawing;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Plugins;

namespace ImageFromUrlPlugin;

public sealed class ImageFromUrlPlugin : MacroDeckPlugin
{
    public static ImageFromUrlPlugin Instance { get; private set; } = null!;

    public override bool CanConfigure => true;

    public ImageFromUrlPlugin()
    {
        Instance = this;
        Actions = new List<PluginAction>
        {
            new SpotifyImageAction()
        };
    }

    public override void Enable()
    {
        MacroDeckLogger.Info(this, "ImageFromUrlPlugin enabled.");
    }

    public override void OpenConfigurator()
    {
        using var form = new SpotifyCredentialsForm(this);
        form.ShowDialog();
    }
}
