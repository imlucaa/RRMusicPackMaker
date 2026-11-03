namespace RRMusicPackMaker;

public enum ExportChoice { None, Folder, Zip, Game }

/// <summary>Asks where the finished pack should go.</summary>
public sealed class ExportForm : Form
{
    private readonly ModernButton _folder = new() { Text = "Choose folder", AutoSize = true, Tag = Theme.AccentTag };
    private readonly ModernButton _zip = new() { Text = "Save as zip", AutoSize = true };
    private readonly ModernButton _game = new() { Text = "Add to game", AutoSize = true, Tag = Theme.AccentTag };
    private readonly ModernButton _cancel = new() { Text = "Cancel", AutoSize = true };

    private readonly ModernButton _forWii = new() { Text = "Wii", AutoSize = true };
    private readonly ModernButton _forDolphin = new() { Text = "Dolphin", AutoSize = true };
    private readonly Label _targetHelp = new() { AutoSize = true, Tag = Theme.MutedTag, Margin = new Padding(0, 1, 0, 0) };

    public ExportChoice Choice { get; private set; }

    /// <summary>True when the pack is for a real Wii, so songs above what it plays are brought down. False keeps them as they are, for Dolphin.</summary>
    public bool ForWii { get; private set; }

    public ExportForm(bool gameFound, bool forWii = true)
    {
        ForWii = forWii;
        Text = "Export pack";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = Theme.UiFont();

        var page = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(22, 20, 22, 18) };
        page.Controls.Add(new Label { Text = "Where should the pack go?", AutoSize = true, Font = Theme.TitleFont(14f), Margin = new Padding(0, 0, 0, 14) });
        page.Controls.Add(TargetCard());
        page.Controls.Add(Card("Save the pack", "To your own folder, or as a zip to share.", _folder, _zip));
        page.Controls.Add(Card("Add to the game",
            gameFound ? "Replaces the songs in your Retro Rewind strm folder, and saves the ones it replaces so you can take it out again."
            : "Needs your Retro Rewind folder. Set it in Settings first.", _game));
        _game.Enabled = gameFound;

        var footer = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 6, 0, 0) };
        footer.Controls.Add(_cancel);
        page.Controls.Add(footer);
        Controls.Add(page);

        _forWii.Click += (_, _) => ChooseTarget(true);
        _forDolphin.Click += (_, _) => ChooseTarget(false);
        ChooseTarget(forWii);
        _folder.Click += (_, _) => Finish(ExportChoice.Folder);
        _zip.Click += (_, _) => Finish(ExportChoice.Zip);
        _game.Click += (_, _) => Finish(ExportChoice.Game);
        _cancel.Click += (_, _) => Finish(ExportChoice.None);
        CancelButton = _cancel;
        Theme.Apply(this);
    }

    private void ChooseTarget(bool forWii)
    {
        ForWii = forWii;
        _forWii.Tag = forWii ? Theme.ActiveTag : Theme.GhostTag;
        _forDolphin.Tag = forWii ? Theme.GhostTag : Theme.ActiveTag;
        _forWii.Invalidate();
        _forDolphin.Invalidate();
        _targetHelp.Text = forWii
            ? "Songs above 44100 Hz are converted down, because a Wii can't play more than that."
            : "Songs are kept exactly as they are. Dolphin plays higher rates too.";
    }

    private CardPanel TargetCard()
    {
        var card = new CardPanel { Width = 580, Height = 72, Padding = new Padding(16, 12, 16, 8), Margin = new Padding(0, 0, 0, 10) };
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var text = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Fill, Margin = new Padding(0) };
        text.Controls.Add(new Label { Text = "Made for", AutoSize = true, Font = Theme.TitleFont(10f), Margin = new Padding(0) });
        text.Controls.Add(_targetHelp);
        grid.Controls.Add(text, 0, 0);
        var choices = new SegmentedBar { Anchor = AnchorStyles.Right, Margin = new Padding(10, 0, 0, 0) };
        choices.Controls.AddRange([_forWii, _forDolphin]);
        grid.Controls.Add(choices, 1, 0);
        card.Controls.Add(grid);
        return card;
    }

    private void Finish(ExportChoice choice)
    {
        Choice = choice;
        DialogResult = choice == ExportChoice.None ? DialogResult.Cancel : DialogResult.OK;
        Close();
    }

    private static CardPanel Card(string title, string help, params ModernButton[] actions)
    {
        var card = new CardPanel { Width = 580, Height = 72, Padding = new Padding(16, 12, 16, 8), Margin = new Padding(0, 0, 0, 10) };
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var text = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Fill, Margin = new Padding(0) };
        text.Controls.Add(new Label { Text = title, AutoSize = true, Font = Theme.TitleFont(10f), Margin = new Padding(0) });
        text.Controls.Add(new Label { Text = help, AutoSize = true, Tag = Theme.MutedTag, Margin = new Padding(0, 1, 0, 0) });
        grid.Controls.Add(text, 0, 0);
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = new Padding(10, 0, 0, 0) };
        foreach (var button in actions)
        {
            button.Margin = new Padding(6, 0, 0, 0);
            buttons.Controls.Add(button);
        }
        grid.Controls.Add(buttons, 1, 0);
        card.Controls.Add(grid);
        return card;
    }

    // For the automated tests.
    internal bool GameEnabledForTest => _game.Enabled;
    internal void ChooseTargetForTest(bool forWii) => ChooseTarget(forWii);
}
