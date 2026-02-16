using System.Drawing;
using System.Windows.Forms;
using SuchByte.MacroDeck.Plugins;

namespace SpotifyAlbumCoverPlugin;

public sealed class SpotifyCredentialsForm : Form
{
    private readonly SpotifyAlbumCoverPlugin _plugin;
    private readonly TextBox _clientIdTextBox;
    private readonly TextBox _clientSecretTextBox;

    public SpotifyCredentialsForm(SpotifyAlbumCoverPlugin plugin)
    {
        _plugin = plugin;

        Text = "Spotify API Credentials";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(420, 170);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(10)
        };
        _ = layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        _ = layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var clientIdLabel = new Label { Text = "Client ID", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        var clientSecretLabel = new Label { Text = "Client Secret", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };

        _clientIdTextBox = new TextBox { Dock = DockStyle.Fill };
        _clientSecretTextBox = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true };

        var currentId = PluginConfiguration.GetValue(_plugin, "SpotifyClientId") ?? string.Empty;
        var currentSecret = PluginConfiguration.GetValue(_plugin, "SpotifyClientSecret") ?? string.Empty;
        _clientIdTextBox.Text = currentId;
        _clientSecretTextBox.Text = currentSecret;

        var saveButton = new Button { Text = "Save", DialogResult = DialogResult.OK, Anchor = AnchorStyles.Right };
        var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Anchor = AnchorStyles.Right };
        saveButton.Click += (_, _) => SaveAndClose();

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft
        };
        buttonPanel.Controls.Add(saveButton);
        buttonPanel.Controls.Add(cancelButton);

        layout.Controls.Add(clientIdLabel, 0, 0);
        layout.Controls.Add(_clientIdTextBox, 1, 0);
        layout.Controls.Add(clientSecretLabel, 0, 1);
        layout.Controls.Add(_clientSecretTextBox, 1, 1);
        layout.Controls.Add(buttonPanel, 0, 3);
        layout.SetColumnSpan(buttonPanel, 2);

        Controls.Add(layout);
    }

    private void SaveAndClose()
    {
        PluginConfiguration.SetValue(_plugin, "SpotifyClientId", _clientIdTextBox.Text.Trim());
        PluginConfiguration.SetValue(_plugin, "SpotifyClientSecret", _clientSecretTextBox.Text.Trim());
        Close();
    }
}


