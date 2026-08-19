namespace VoicePromptTray;

internal sealed class CorrectionLearningDialog : Form
{
    private readonly TextBox _heard = new() { MaxLength = 120, Dock = DockStyle.Fill };
    private readonly TextBox _replacement = new() { MaxLength = 120, Dock = DockStyle.Fill };

    public string Heard => _heard.Text.Trim();
    public string Replacement => _replacement.Text.Trim();

    public CorrectionLearningDialog(string heard = "", string replacement = "")
    {
        Text = "Learn correction";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(520, 230);
        BackColor = Theme.Canvas;
        ForeColor = Theme.Text;
        Font = Theme.Font();

        _heard.Text = heard;
        _replacement.Text = replacement;
        _heard.AccessibleName = "What VoicePrompt heard";
        _replacement.AccessibleName = "What VoicePrompt should write";
        foreach (TextBox field in new[] { _heard, _replacement })
        {
            field.BackColor = Theme.Control;
            field.ForeColor = Theme.Text;
            field.BorderStyle = BorderStyle.FixedSingle;
        }

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22),
            ColumnCount = 1,
            RowCount = 6,
            BackColor = Theme.Canvas,
        };
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        grid.Controls.Add(LabelFor("VoicePrompt heard"), 0, 0);
        grid.Controls.Add(_heard, 0, 1);
        grid.Controls.Add(LabelFor("Write this instead"), 0, 2);
        grid.Controls.Add(_replacement, 0, 3);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Theme.Canvas,
        };
        var save = new Button { Text = "Learn", DialogResult = DialogResult.OK, Width = 92, Height = 34 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 92, Height = 34 };
        actions.Controls.Add(save);
        actions.Controls.Add(cancel);
        grid.Controls.Add(actions, 0, 5);
        Controls.Add(grid);
        AcceptButton = save;
        CancelButton = cancel;
        Shown += (_, _) => _heard.Focus();
    }

    private static Label LabelFor(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        ForeColor = Theme.Muted,
        TextAlign = ContentAlignment.MiddleLeft,
    };
}
