namespace RRMusicPackMaker;

public enum ExportChoice { None, Folder, Zip, WheelWizard }

/// <summary>Asks where the finished pack should go.</summary>
public sealed class ExportForm : Form
{
    private readonly ModernButton _folder = new() { Text = "Choose folder", AutoSize = true, Tag = Theme.AccentTag };
    private readonly ModernButton _zip = new() { Text = "Save as zip", AutoSize = true };
    private readonly ModernButton _wheelWizard = new() { Text = "Add", AutoSize = true };
    private readonly ModernButton _cancel = new() { Text = "Cancel", AutoSize = true };

    public ExportChoice Choice { get; private set; }

    public ExportForm(bool wheelWizardFound)
    {
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
        page.Controls.Add(Card("Save the pack", "For a real Wii, Dolphin or sharing.", _folder, _zip));
        page.Controls.Add(Card("Add to Wheel Wizard",
            wheelWizardFound ? "Adds it to the Mods list in Wheel Wizard or Wheel Wizard+." : "Wheel Wizard wasn't found on this PC.", _wheelWizard));
        _wheelWizard.Enabled = wheelWizardFound;

        var footer = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 6, 0, 0) };
        footer.Controls.Add(_cancel);
        page.Controls.Add(footer);
        Controls.Add(page);

        _folder.Click += (_, _) => Finish(ExportChoice.Folder);
        _zip.Click += (_, _) => Finish(ExportChoice.Zip);
        _wheelWizard.Click += (_, _) => Finish(ExportChoice.WheelWizard);
        _cancel.Click += (_, _) => Finish(ExportChoice.None);
        CancelButton = _cancel;
        Theme.Apply(this);
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
    internal bool WheelWizardEnabledForTest => _wheelWizard.Enabled;
}
