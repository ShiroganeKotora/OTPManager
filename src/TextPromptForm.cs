namespace OtpManager;

/// <summary>A one-line text prompt, for the few places a whole dialog would be overkill.</summary>
internal sealed class TextPromptForm : Form
{
    private readonly TextBox _input = new() { Dock = DockStyle.Fill };

    public string Value => _input.Text.Trim();

    public TextPromptForm(string title, string label, string initial = "", bool masked = false)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(360, 116);

        var caption = new Label { Text = label, Dock = DockStyle.Top, Height = 24 };
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 12, 14, 0) };
        body.Controls.Add(_input);
        body.Controls.Add(caption);

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 96 };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Width = 96 };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 48,
            Padding = new Padding(14, 8, 14, 8),
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        Controls.Add(body);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;

        _input.UseSystemPasswordChar = masked;
        _input.Text = initial;
        Shown += (_, _) => { _input.Focus(); _input.SelectAll(); };

        Theme.Style(this);
    }
}
