using System.Drawing;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Plugins;

namespace SpotifyAlbumCoverPlugin;

public sealed class SpotifyAlbumCoverPlugin : MacroDeckPlugin
{
    public static SpotifyAlbumCoverPlugin Instance { get; private set; } = null!;

    public override bool CanConfigure => true;

    public SpotifyAlbumCoverPlugin()
    {
        Instance = this;
        Actions = new List<PluginAction>
        {
            new SpotifyImageAction()
        };
    }

    public override void Enable()
    {
        MacroDeckLogger.Info(this, "SpotifyAlbumCoverPlugin enabled.");

        // Wir suchen das Event manuell, um Compiler-Fehler zu vermeiden
        var eventInfo = typeof(SuchByte.MacroDeck.Variables.VariableManager).GetEvent("VariableChanged")
                     ?? typeof(SuchByte.MacroDeck.Variables.VariableManager).GetEvent("OnVariableChanged");

        if (eventInfo != null)
        {
            var methodInfo = typeof(SpotifyAlbumCoverPlugin).GetMethod("OnVariableChanged", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (methodInfo != null && eventInfo.EventHandlerType != null && eventInfo.AddMethod != null)
            {
                var handler = Delegate.CreateDelegate(eventInfo.EventHandlerType, this, methodInfo);
                _ = eventInfo.AddMethod.Invoke(null, new object[] { handler });
            }
        }
    }

    // Die Methode muss genau diese Signatur haben (object, EventArgs oder dynamic)
    private void OnVariableChanged(object sender, EventArgs e)
    {
        try
        {
            // Wir nutzen dynamic, um auf die Property 'Variable' zuzugreifen
            dynamic ev = e;
            if (ev.Variable.Name == "spotify_playing_title")
            {
                SpotifyImageAction.TriggerUpdateForAllButtons();
            }
        }
        catch { /* Falls die Property nicht existiert */ }
    }

    public override void OpenConfigurator()
    {
        using var form = new SpotifyCredentialsForm(this);
        _ = form.ShowDialog();
    }
}


