using System.Windows.Forms;
using SuchByte.MacroDeck.GUI.CustomControls;

namespace SpotifyAlbumCoverPlugin;

public sealed class SpotifyImageActionConfigControl : ActionConfigControl
{
    private readonly SpotifyImageAction _action;
    private readonly TextBox _titleTextBox;
    private readonly TextBox _artistTextBox;
    private readonly NumericUpDown _refreshSecondsInput;
    private readonly CheckBox _onlyIfMissingCheckBox;

    public SpotifyImageActionConfigControl(SpotifyImageAction action)
    {
        _action = action;
        Dock = DockStyle.Fill;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            Padding = new Padding(6),
            AutoSize = true
        };
        _ = layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        _ = layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        _ = layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        _ = layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        _ = layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        _ = layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        _ = layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var titleLabel = new Label { Text = "Title", Dock = DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
        var artistLabel = new Label { Text = "Artist", Dock = DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
        var refreshLabel = new Label { Text = "Min refresh (s)", Dock = DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };

        _titleTextBox = new TextBox { Dock = DockStyle.Fill };
        _artistTextBox = new TextBox { Dock = DockStyle.Fill };
        _refreshSecondsInput = new NumericUpDown
        {
            Dock = DockStyle.Left,
            Minimum = 5,
            Maximum = 86400,
            Value = 300,
            Width = 100
        };
        _onlyIfMissingCheckBox = new CheckBox
        {
            Text = "Only update if icon is missing",
            Dock = DockStyle.Fill,
            AutoSize = true
        };

        var helpLabel = new Label
        {
            Text = "Supports Macro Deck templates/variables (Cottle) in both fields.",
            Dock = DockStyle.Fill,
            AutoSize = true
        };

        layout.Controls.Add(titleLabel, 0, 0);
        layout.Controls.Add(_titleTextBox, 1, 0);
        layout.Controls.Add(artistLabel, 0, 1);
        layout.Controls.Add(_artistTextBox, 1, 1);
        layout.Controls.Add(refreshLabel, 0, 2);
        layout.Controls.Add(_refreshSecondsInput, 1, 2);
        layout.Controls.Add(_onlyIfMissingCheckBox, 0, 3);
        layout.SetColumnSpan(_onlyIfMissingCheckBox, 2);
        layout.Controls.Add(helpLabel, 0, 4);
        layout.SetColumnSpan(helpLabel, 2);

        Controls.Add(layout);

        var config = _action.GetConfig();
        _titleTextBox.Text = config.Title;
        _artistTextBox.Text = config.Artist;
        _refreshSecondsInput.Value = Math.Clamp(config.MinRefreshSeconds, 5, 86400);
        _onlyIfMissingCheckBox.Checked = config.OnlyUpdateIfMissing;
    }

    public override bool OnActionSave()
    {
        var config = new SpotifyImageActionConfig
        {
            Title = _titleTextBox.Text.Trim(),
            Artist = _artistTextBox.Text.Trim(),
            MinRefreshSeconds = (int)_refreshSecondsInput.Value,
            OnlyUpdateIfMissing = _onlyIfMissingCheckBox.Checked
        };

        _action.UpdateConfiguration(config);
        return true;
    }
}


