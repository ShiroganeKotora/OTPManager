using System.Reflection;

namespace OtpManager;

/// <summary>Name, version and licences. Nothing else - the README is where explanations live.</summary>
internal sealed class AboutForm : Form
{
    public AboutForm()
    {
        Text = "バージョン情報";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(420, 258);

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?";

        var name = new Label
        {
            Text = "OTP Manager",
            Dock = DockStyle.Top,
            Height = 30,
            Font = new Font(Font.FontFamily, 14f, FontStyle.Bold),
        };
        var release = new Label { Text = $"Version {version}", Dock = DockStyle.Top, Height = 22 };
        var copyright = new Label { Text = "Copyright (C) 2026 Shirogane Kotora", Dock = DockStyle.Top, Height = 30 };

        var licences = new Label
        {
            Dock = DockStyle.Top,
            Height = 104,
            ForeColor = Color.FromArgb(70, 76, 84),
            Text = string.Join(Environment.NewLine,
            [
                "OTP_OriGlyph  Copyright (C) 2026 OTP Manager",
                "  Apache License 2.0",
                "ZXing.Net  Copyright (C) ZXing.Net Authors",
                "  Apache License 2.0",
                "Material Symbols  Copyright (C) Google LLC",
                "  Apache License 2.0",
            ]),
        };

        // Docked panels stack from the last control added, so build the column bottom-up.
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18, 16, 18, 0) };
        body.Controls.Add(licences);
        body.Controls.Add(copyright);
        body.Controls.Add(release);
        body.Controls.Add(name);

        var close = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 100 };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 48,
            Padding = new Padding(18, 8, 18, 8),
        };
        buttons.Controls.Add(close);

        Controls.Add(body);
        Controls.Add(buttons);
        AcceptButton = close;
        CancelButton = close;

        Theme.Style(this);
    }
}
