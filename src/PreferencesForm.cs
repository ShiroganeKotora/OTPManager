namespace OtpManager;

/// <summary>Categories on the left, the chosen page on the right.</summary>
internal sealed partial class PreferencesForm : Form
{
    private readonly AccountStore _store;
    private readonly Action _dataChanged;
    private readonly Size _listViewport;

    private readonly ListBox _categories = new()
    {
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.None,
        IntegralHeight = false,
        ItemHeight = 26,
        BackColor = ListStyle.SidebarBackground,
    };

    private readonly Panel _pages = new() { Dock = DockStyle.Fill, Padding = new Padding(16, 12, 16, 12) };
    private readonly List<Panel> _panels = [];

    public PreferencesForm(AccountStore store, Action dataChanged, Size listViewport)
    {
        _store = store;
        _dataChanged = dataChanged;
        _listViewport = listViewport;

        Text = "環境設定";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(660, 420);

        _panels.Add(BuildGeneral());
        _panels.Add(BuildDisplay());
        _panels.Add(BuildBackground());
        _panels.Add(BuildBackup());
        _panels.Add(BuildClock());
        _panels.Add(BuildSource());

        foreach(var panel in _panels)
        {
            panel.Dock = DockStyle.Fill;
            panel.Visible = false;
            _pages.Controls.Add(panel);
        }

        _categories.Items.AddRange(["全般", "表示", "背景", "バックアップ", "時刻の同期", "ソースコード"]);
        _categories.SelectedIndexChanged += (_, _) => ShowPage(_categories.SelectedIndex);

        var left = new Panel
        {
            Dock = DockStyle.Left,
            Width = 168,
            Padding = new Padding(0, 8, 0, 8),
            BackColor = ListStyle.SidebarBackground,
            Tag = Theme.SidebarTag,
        };
        left.Controls.Add(_categories);

        var split = new Panel { Dock = DockStyle.Fill };
        split.Controls.Add(_pages);
        split.Controls.Add(left);

        var close = new Button { Text = "閉じる", DialogResult = DialogResult.OK, Width = 100 };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 48,
            Padding = new Padding(14, 8, 14, 8),
        };
        buttons.Controls.Add(close);

        Controls.Add(split);
        Controls.Add(buttons);
        AcceptButton = close;
        CancelButton = close;

        _categories.SelectedIndex = 0;
        Theme.Style(this);
    }

    private void ShowPage(int index)
    {
        for(var i = 0; i < _panels.Count; i++) _panels[i].Visible = i == index;
    }

    // --- shared pieces ---------------------------------------------------

    private static Label Heading(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Top,
        Height = 30,
        Font = new Font(SystemFonts.DefaultFont.FontFamily, 11f, FontStyle.Bold),
    };

    private static Label Note(string text, int height) => new()
    {
        Text = text,
        Dock = DockStyle.Top,
        Height = height,
        ForeColor = ListStyle.SubtleText,
        Tag = Theme.SubtleTag,
    };

    private static Button Action(string text, EventHandler onClick)
    {
        var button = new Button { Text = text, Width = 150, Height = 30, FlatStyle = FlatStyle.System, Margin = new Padding(0, 0, 8, 0) };
        button.Click += onClick;
        return button;
    }

    private static Panel Row(params Control[] controls)
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, FlowDirection = FlowDirection.LeftToRight };
        flow.Controls.AddRange(controls);
        return flow;
    }

    /// <summary>Docked panels stack from the last control added, so pages are built bottom-up.</summary>
    private static Panel Page(params Control[] topDown)
    {
        var panel = new Panel();
        for(var i = topDown.Length - 1; i >= 0; i--) panel.Controls.Add(topDown[i]);
        return panel;
    }

    private void Warn(string message) =>
        MessageBox.Show(this, message, "OTP Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private void Inform(string message) =>
        MessageBox.Show(this, message, "OTP Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
}
