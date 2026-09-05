namespace OtpManager;

internal sealed class EditAccountForm : Form
{
    private readonly TextBox _issuer = new();
    private readonly TextBox _name = new();
    private readonly TextBox _secret = new();
    private readonly NumericUpDown _digits = new() { Minimum = 6, Maximum = 10, Value = 6 };
    private readonly NumericUpDown _period = new() { Minimum = 10, Maximum = 300, Increment = 5, Value = 30 };
    private readonly ComboBox _algorithm = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _group = new() { DropDownStyle = ComboBoxStyle.DropDown };
    private readonly Label _hint = new() { ForeColor = ListStyle.SubtleText, AutoSize = false, Tag = Theme.SubtleTag };

    public Account Result { get; private set; } = new();

    /// <summary>The typed or picked group, with the placeholder mapped back to "no group".</summary>
    private string GroupValue
    {
        get
        {
            var text = _group.Text.Trim();
            return text is "（未分類）" or "(未分類)" ? "" : text;
        }
    }

    public EditAccountForm(Account? existing, IEnumerable<string> groups)
    {
        Text = existing == null ? "アカウントを追加" : "アカウントを編集";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(460, 336);

        _algorithm.Items.AddRange(Totp.Algorithms);
        _algorithm.SelectedIndex = 0;

        // Editable, so typing a name that does not exist yet creates that group.
        _group.Items.Add("（未分類）");
        foreach(var group in groups) _group.Items.Add(group);
        _group.SelectedIndex = 0;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(14), RowCount = 7 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        void Row(string label, Control control)
        {
            control.Dock = DockStyle.Fill;
            layout.Controls.Add(new Label { Text = label, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill });
            layout.Controls.Add(control);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        }

        Row("発行者", _issuer);
        Row("アカウント名", _name);
        Row("シークレット", _secret);

        _hint.Text = "otpauth:// で始まるURIをシークレット欄に貼り付けると、他の項目も自動で埋まります。";
        _hint.Dock = DockStyle.Fill;
        layout.Controls.Add(new Label());
        layout.Controls.Add(_hint);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        Row("グループ", _group);
        Row("桁数", _digits);
        Row("周期（秒）", _period);
        Row("アルゴリズム", _algorithm);

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

        Controls.Add(layout);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;

        // Pasting a whole otpauth URI is the common path, so fill the rest of the form from it immediately.
        _secret.TextChanged += (_, _) =>
        {
            if(!_secret.Text.TrimStart().StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase)) return;
            if(!Account.TryParseUri(_secret.Text, out var parsed, out _)) return;
            _issuer.Text = parsed.Issuer;
            _name.Text = parsed.Name;
            _digits.Value = parsed.Digits;
            _period.Value = parsed.Period;
            _algorithm.SelectedItem = parsed.Algorithm;
            _secret.Text = parsed.Secret;
            _secret.SelectionStart = _secret.TextLength;
        };

        if(existing == null)
        {
            // The values almost every service issues, so a new account only needs its secret.
            _digits.Value = 6;
            _period.Value = 30;
            _algorithm.SelectedItem = "SHA1";
            Shown += (_, _) => _secret.Focus();
        }
        else
        {
            _issuer.Text = existing.Issuer;
            _name.Text = existing.Name;
            _secret.Text = existing.Secret;
            _digits.Value = existing.Digits;
            _period.Value = existing.Period;
            _algorithm.SelectedItem = existing.Algorithm;
            _group.Text = existing.Group.Length > 0 ? existing.Group : "（未分類）";
        }

        FormClosing += (_, e) =>
        {
            if(DialogResult != DialogResult.OK) return;
            if(!Base32.IsValid(_secret.Text))
            {
                MessageBox.Show(this, "シークレットがBase32として解釈できません。", "OTP Manager",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                e.Cancel = true;
                return;
            }
            if(_issuer.Text.Trim().Length == 0 && _name.Text.Trim().Length == 0)
            {
                MessageBox.Show(this, "発行者かアカウント名のどちらかは入力してください。", "OTP Manager",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                e.Cancel = true;
                return;
            }
            Result = new Account
            {
                Group = GroupValue,
                Issuer = _issuer.Text.Trim(),
                Name = _name.Text.Trim(),
                Secret = _secret.Text.Trim().Replace(" ", "").ToUpperInvariant(),
                Digits = (int)_digits.Value,
                Period = (int)_period.Value,
                Algorithm = (string)_algorithm.SelectedItem!,
            };
        };

        Theme.Style(this);
    }
}
