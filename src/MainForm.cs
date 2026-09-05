using System.Text.Json;

namespace OtpManager;

internal sealed class MainForm : Form
{
    private readonly AccountStore _store = new();
    private readonly ListPanel _list = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = ListStyle.ListBackground };
    private readonly TextBox _filter = new() { Dock = DockStyle.Fill, PlaceholderText = "絞り込み" };
    private readonly Toast _toast = new();
    private readonly Label _empty = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(140, 140, 140), Visible = false, Text = "アカウントがありません。\n「追加」から登録してください。" };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 50 };
    private readonly List<AccountRow> _rows = [];
    private readonly List<GroupHeader> _headers = [];
    private readonly List<Control> _items = [];
    private readonly Panel _insertion = new() { BackColor = Color.FromArgb(0, 120, 215), Visible = false };

    private readonly ContextMenuStrip _qrMenu = new();
    private Account? _qrAccount;
    private readonly Panel _qrCard = new() { BackColor = Color.White, Visible = false };
    private readonly PictureBox _qrImage = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White };
    private readonly Label _qrTitle = new() { Dock = DockStyle.Top, Height = 26, TextAlign = ContentAlignment.MiddleCenter };
    private readonly Label _qrNote = new() { Dock = DockStyle.Bottom, Height = 40, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(180, 60, 20), Text = "このQRコード表示中は、\n画面共有などを行わないでください" };
    private Bitmap? _qrBitmap;

    private Control? _dragItem;
    private int _dropIndex = -1;
    private string _dropGroup = "";

    private TableLayoutPanel _toolbar = null!;

    public MainForm()
    {
        Text = "OTP Manager";

        // Cards are laid out for one fixed width, so only the height is the user's to change.
        // The width is held by SetBoundsCore rather than MaximumSize, whose documented "0 means
        // unlimited" does not hold for the height once the width is set.
        MinimumSize = new Size(FixedWidth, 240);
        MaximizeBox = false;
        Size = new Size(FixedWidth, 640);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = ListStyle.ListBackground;
        KeyPreview = true;

        // Buttons are sized to the glyph itself, which leaves room for a third across the toolbar.
        const int buttonWidth = 28;

        var add = new IconButton(OriGlyphs.QrCodeEdit, OriGlyphs.Get(22f)) { Dock = DockStyle.Right, Width = buttonWidth };
        add.Click += (_, _) => AddAccount();

        var import = new IconButton(MaterialSymbols.QrCodeAdd, MaterialSymbols.Get(22f)) { Dock = DockStyle.Right, Width = buttonWidth };
        var importMenu = new ContextMenuStrip();
        importMenu.Items.Add("画像ファイルから(&F)...", null, (_, _) => ImportFromFile());
        importMenu.Items.Add("クリップボードから(&C)", null, (_, _) => ImportFromClipboard());
        import.Click += (_, _) => importMenu.Show(import, new Point(0, import.Height));

        var menu = new IconButton(MaterialSymbols.Toc, MaterialSymbols.Get(22f)) { Dock = DockStyle.Right, Width = buttonWidth };
        var mainMenu = new ContextMenuStrip { ImageScalingSize = new Size(18, 18) };
        var menuFont = MaterialSymbols.Get(18f);
        var menuColor = Color.FromArgb(60, 70, 82);
        mainMenu.Items.Add("グループの作成(&G)...", GlyphImage.Render(MaterialSymbols.AdGroup, menuFont, 18, menuColor),
            (_, _) => AddGroup());
        mainMenu.Items.Add("アプリについて(&A)...", GlyphImage.Render(MaterialSymbols.QuickReference, menuFont, 18, menuColor),
            (_, _) => ShowAbout());
        mainMenu.Items.Add("環境設定(&P)...", GlyphImage.Render(MaterialSymbols.SettingsApplications, menuFont, 18, menuColor),
            (_, _) => ShowPreferences());
        menu.Click += (_, _) => mainMenu.Show(menu, new Point(0, menu.Height));

        var tips = new ToolTip();
        tips.SetToolTip(add, "アカウントを手入力で追加");
        tips.SetToolTip(import, "QRコードから読み込む");
        tips.SetToolTip(menu, "メニュー");

        _toolbar = new TableLayoutPanel { Dock = DockStyle.Top, Height = 40, ColumnCount = 4, Padding = new Padding(10, 7, 10, 5) };
        _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for(var i = 0; i < 3; i++) _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _toolbar.Controls.Add(_filter, 0, 0);
        _toolbar.Controls.Add(import, 1, 0);
        _toolbar.Controls.Add(add, 2, 0);
        _toolbar.Controls.Add(menu, 3, 0);

        _list.Controls.Add(_empty);
        _list.Controls.Add(_insertion);
        BuildQrPanel();
        Controls.Add(_list);
        Controls.Add(_toolbar);
        Controls.Add(_toast);

        // AutoScrollMargin, not Padding: only the margin is added to the scrollable extent, so this
        // is what stops the last card sitting flush against the bottom edge.
        _list.AutoScrollMargin = new Size(0, ListStyle.CardGap);
        _list.ClientSizeChanged += (_, _) => LayoutItems();

        _filter.TextChanged += (_, _) => Rebuild();
        _timer.Tick += (_, _) => Tick();

        // Closing the window keeps the app alive in the tray; only the tray menu really quits.
        FormClosing += (_, e) =>
        {
            if(e.CloseReason != CloseReason.UserClosing) return;
            e.Cancel = true;
            HideToTray();
        };

        KeyDown += (_, e) =>
        {
            if(e.KeyCode == Keys.Escape)
            {
                // Escape closes the QR panel first, and only then puts the window away.
                if(_qrCard.Visible) CloseQr();
                else HideToTray();
            }
            if(e.Control && e.KeyCode == Keys.F) _filter.Focus();
        };

        Theme.Changed += ApplyTheme;
        Background.Changed += RepaintList;
        ApplyTheme();
        Background.Load();

        LoadWindowBounds();
        try
        {
            _store.Load();
        }
        catch(Exception ex)
        {
            MessageBox.Show(this, $"設定の読み込みに失敗しました。\n\n{ex.Message}", "OTP Manager",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        Rebuild();
        _timer.Start();
    }

    /// <summary>Repaints everything the palette touches. Called at start-up and whenever it changes.</summary>
    private void ApplyTheme()
    {
        BackColor = ListStyle.ListBackground;
        _list.BackColor = _qrCard.Visible ? ListStyle.DimBackground : ListStyle.ListBackground;
        _filter.BackColor = ListStyle.InputBackground;
        _filter.ForeColor = ListStyle.DialogText;
        _empty.ForeColor = ListStyle.SubtleText;
        _insertion.BackColor = ListStyle.Highlight;

        _qrCard.BackColor = ListStyle.CardFill;
        _qrImage.BackColor = Color.White;   // a QR code has to stay black on white to scan
        _qrTitle.ForeColor = ListStyle.DialogText;
        _qrNote.ForeColor = ListStyle.Warning;

        Theme.ApplyToTitleBar(this);
        foreach(Control control in Controls) control.Invalidate(true);
        Invalidate(true);
    }

    /// <summary>Throws away the rendered backdrop and redraws, after the picture or framing changed.</summary>
    private void RepaintList()
    {
        _list.Invalidate(true);
        foreach(Control control in _list.Controls) control.Invalidate();
    }

    private void Tick()
    {
        var now = Settings.Current.NowUnixMilliseconds();
        foreach(var row in _rows) row.Tick(now);
        // The toast floats over the list, so it has to be nudged while it slides into place.
        if(_toast.Tick() && _toast.Sliding) PlaceToast();
    }

    private void Rebuild()
    {
        _list.SuspendLayout();
        foreach(var row in _rows) { _list.Controls.Remove(row); row.Dispose(); }
        _rows.Clear();
        foreach(var header in _headers) { _list.Controls.Remove(header); header.Dispose(); }
        _headers.Clear();
        _items.Clear();

        var filter = _filter.Text.Trim();
        bool Matches(Account a) => filter.Length == 0 || a.Title.Contains(filter, StringComparison.OrdinalIgnoreCase);

        var byId = _store.Accounts.Where(a => a.Id.Length > 0).ToDictionary(a => a.Id);
        var byName = _store.Groups.ToDictionary(g => g.Name, StringComparer.Ordinal);
        var total = _store.Accounts.Where(a => a.Group.Length > 0)
            .GroupBy(a => a.Group).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var hits = _store.Accounts.Where(a => a.Group.Length > 0 && Matches(a))
            .GroupBy(a => a.Group).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var items = new List<Control>();
        AccountGroup? current = null;

        foreach(var token in _store.Order)
        {
            if(ListOrder.IsGroup(token))
            {
                current = byName.GetValueOrDefault(ListOrder.GroupName(token));
                if(current == null) continue;

                // While filtering, a group with nothing to show is left out entirely.
                if(filter.Length > 0 && hits.GetValueOrDefault(current.Name) == 0) continue;
                items.Add(CreateHeader(current, total.GetValueOrDefault(current.Name)));
                continue;
            }
            if(!ListOrder.IsAccount(token)) continue;
            if(!byId.TryGetValue(ListOrder.AccountId(token), out var account)) continue;

            if(account.Group.Length == 0) current = null;
            if(!Matches(account)) continue;

            // A filter overrides the fold; a match must never stay hidden inside a closed group.
            if(account.Group.Length > 0 && current is { Collapsed: true } && filter.Length == 0) continue;
            items.Add(CreateRow(account));
        }

        MarkOutlines(items);

        _items.Clear();
        _items.AddRange(items);
        _list.Controls.AddRange([.. items]);
        LayoutItems();

        _empty.Visible = _store.Accounts.Count == 0;
        _empty.BringToFront();
        _list.ResumeLayout();
        Tick();
    }

    /// <summary>
    /// Works out who draws which part of each group outline: the heading draws the top and sides,
    /// members draw the sides, and whoever comes last in the group closes the bottom.
    /// </summary>
    private static void MarkOutlines(List<Control> items)
    {
        for(var i = 0; i < items.Count; i++)
        {
            if(items[i] is not AccountRow row) continue;

            row.InGroup = row.Account.Group.Length > 0;
            if(!row.InGroup) { row.LastInGroup = false; continue; }

            var following = i + 1 < items.Count ? items[i + 1] : null;
            row.LastInGroup = following is not AccountRow other || other.Account.Group != row.Account.Group;
        }

        // A heading only closes its own outline when no member of that group follows it.
        for(var i = 0; i < items.Count; i++)
        {
            if(items[i] is not GroupHeader header) continue;
            var next = i + 1 < items.Count ? items[i + 1] : null;
            header.ClosesOutline = next is not AccountRow row || row.Account.Group != header.Group.Name;
        }
    }

    /// <summary>
    /// Stacks the rows by hand rather than docking them. Dock inside an AutoScroll panel makes the
    /// layout run again on every scroll, which drags the content along with the viewport; plain
    /// coordinates scroll predictably. Positions are relative to the current scroll offset, because
    /// scrolling moves the child controls themselves.
    /// </summary>
    /// <summary>The one width the window is ever given.</summary>
    private const int FixedWidth = 340;

    private bool _layingOut;

    private void LayoutItems()
    {
        // Resizing the rows can make the scrollbar appear or vanish, which resizes the panel again.
        if(_layingOut || _items.Count == 0) return;
        _layingOut = true;

        var width = _list.ClientSize.Width;
        var offset = _list.AutoScrollPosition.Y;
        var y = 0;

        _list.SuspendLayout();
        foreach(var item in _items)
        {
            item.SetBounds(0, y + offset, width, item.Height);
            y += item.Height;
        }
        _list.ResumeLayout();
        _layingOut = false;
    }

    private AccountRow CreateRow(Account account)
    {
        var row = new AccountRow(account) { Dimmed = _qrCard.Visible };
        row.CopyRequested += (s, _) => Copy((AccountRow)s!);
        row.EditRequested += (s, _) => EditAccount(((AccountRow)s!).Account);
        row.DeleteRequested += (s, _) => DeleteAccount(((AccountRow)s!).Account);
        row.MoveRequested += (s, delta) => MoveAccount(((AccountRow)s!).Account, delta);
        row.QrRequested += (s, _) => ShowQr(((AccountRow)s!).Account);
        row.SaveQrRequested += (s, _) => SaveQr(((AccountRow)s!).Account);
        row.CopyQrImageRequested += (s, _) => CopyQrImage(((AccountRow)s!).Account);
        row.CopyUriRequested += (s, _) => CopyUri(((AccountRow)s!).Account);
        row.DragBegan += (s, point) => BeginDrag((Control)s!, point);
        row.DragMoved += (_, point) => UpdateDrag(point);
        row.DragEnded += (_, _) => EndDrag();
        row.DismissRequested += (_, _) => CloseQr();
        _rows.Add(row);
        return row;
    }

    private GroupHeader CreateHeader(AccountGroup group, int count)
    {
        var header = new GroupHeader(group, count) { Dimmed = _qrCard.Visible };
        header.ToggleRequested += (s, _) => ToggleGroup(((GroupHeader)s!).Group);
        header.RenameRequested += (s, _) => RenameGroup(((GroupHeader)s!).Group);
        header.DeleteRequested += (s, _) => DeleteGroup(((GroupHeader)s!).Group);
        header.MoveRequested += (s, delta) => MoveGroup(((GroupHeader)s!).Group, delta);
        header.DragBegan += (s, point) => BeginDrag((Control)s!, point);
        header.DragMoved += (_, point) => UpdateDrag(point);
        header.DragEnded += (_, _) => EndDrag();
        _headers.Add(header);
        return header;
    }

    private void Copy(AccountRow row)
    {
        var code = row.CurrentCode;
        if(code.Length == 0) { Say("コードを生成できません。"); return; }
        try
        {
            Clipboard.SetText(code);
            Say($"{row.Account.Title} のコードをコピーしました。");
        }
        catch(Exception)
        {
            // The clipboard is occasionally held by another process; one retry clears it in practice.
            try { Clipboard.SetText(code); Say("コピーしました。"); }
            catch(Exception ex) { Say($"コピーに失敗しました: {ex.Message}"); }
        }
    }

    private void Say(string message)
    {
        _toast.Show(message, TimeSpan.FromSeconds(4));
        PlaceToast();
    }

    /// <summary>Centres the toast near the bottom of the window, allowing for its slide-in.</summary>
    private void PlaceToast()
    {
        var margin = 18;
        _toast.Location = new Point((ClientSize.Width - _toast.Width) / 2,
            ClientSize.Height - _toast.Height - margin + _toast.SlideOffset);
        _toast.BringToFront();
    }

    private void AddAccount()
    {
        using var dialog = new EditAccountForm(null, _store.Groups.Select(g => g.Name));
        if(dialog.ShowDialog(this) != DialogResult.OK) return;
        _store.Accounts.Add(dialog.Result);
        Persist();
    }

    private void EditAccount(Account account)
    {
        using var dialog = new EditAccountForm(account, _store.Groups.Select(g => g.Name));
        if(dialog.ShowDialog(this) != DialogResult.OK) return;
        var index = _store.Accounts.IndexOf(account);
        if(index < 0) return;
        _store.Accounts[index] = dialog.Result;
        Persist();
    }

    private void DeleteAccount(Account account)
    {
        var answer = MessageBox.Show(this,
            $"「{account.Title}」を削除しますか？\n\nシークレットは復元できません。" +
            "他の場所にバックアップが無い場合、このアカウントのコードは二度と生成できなくなります。",
            "OTP Manager", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if(answer != DialogResult.Yes) return;
        _store.Accounts.Remove(account);
        Persist();
    }

    /// <summary>Swaps an account with its neighbour, as long as that neighbour is in the same group.</summary>
    private void MoveAccount(Account account, int delta)
    {
        var order = _store.Order;
        var at = order.IndexOf(ListOrder.ForAccount(account));
        var to = at + delta;
        if(at < 0 || to < 0 || to >= order.Count || !ListOrder.IsAccount(order[to])) return;

        var neighbour = _store.Accounts.FirstOrDefault(a => a.Id == ListOrder.AccountId(order[to]));
        if(neighbour == null || neighbour.Group != account.Group) return;

        (order[at], order[to]) = (order[to], order[at]);
        Persist();
    }

    /// <summary>
    /// The list as a sequence of blocks: a group heading with its members, or a single ungrouped
    /// account. Moving whole sections around is done in these terms.
    /// </summary>
    private List<(int Start, int Length)> Blocks()
    {
        var order = _store.Order;
        var byId = _store.Accounts.Where(a => a.Id.Length > 0).ToDictionary(a => a.Id);
        var blocks = new List<(int, int)>();

        var i = 0;
        while(i < order.Count)
        {
            if(!ListOrder.IsGroup(order[i])) { blocks.Add((i, 1)); i++; continue; }

            var name = ListOrder.GroupName(order[i]);
            var length = 1;
            while(i + length < order.Count
                  && ListOrder.IsAccount(order[i + length])
                  && byId.TryGetValue(ListOrder.AccountId(order[i + length]), out var member)
                  && member.Group == name) length++;

            blocks.Add((i, length));
            i += length;
        }
        return blocks;
    }

    // --- groups ----------------------------------------------------------

    private void ShowAbout()
    {
        using var dialog = new AboutForm();
        dialog.ShowDialog(this);
    }

    private void ShowPreferences()
    {
        using var dialog = new PreferencesForm(_store, () => { _store.Repair(); Persist(); }, _list.ClientSize);
        dialog.ShowDialog(this);
    }

    private void AddGroup()
    {
        using var dialog = new TextPromptForm("グループを追加", "グループ名");
        if(dialog.ShowDialog(this) != DialogResult.OK || dialog.Value.Length == 0) return;
        if(_store.Groups.Any(g => g.Name == dialog.Value)) { Warn("同じ名前のグループが既にあります。"); return; }

        _store.Groups.Add(new AccountGroup { Name = dialog.Value });
        Persist();
        Say($"グループ「{dialog.Value}」を追加しました。");
    }

    private void ToggleGroup(AccountGroup group)
    {
        group.Collapsed = !group.Collapsed;
        Persist();
    }

    private void RenameGroup(AccountGroup group)
    {
        using var dialog = new TextPromptForm("グループ名を変更", "新しい名前", group.Name);
        if(dialog.ShowDialog(this) != DialogResult.OK || dialog.Value.Length == 0) return;
        if(dialog.Value == group.Name) return;
        if(_store.Groups.Any(g => g.Name == dialog.Value)) { Warn("同じ名前のグループが既にあります。"); return; }

        foreach(var account in _store.Accounts.Where(a => a.Group == group.Name)) account.Group = dialog.Value;

        var token = _store.Order.IndexOf(ListOrder.ForGroup(group.Name));
        group.Name = dialog.Value;
        if(token >= 0) _store.Order[token] = ListOrder.ForGroup(group.Name);
        Persist();
    }

    private void DeleteGroup(AccountGroup group)
    {
        var members = _store.Accounts.Count(a => a.Group == group.Name);
        var answer = MessageBox.Show(this,
            $"グループ「{group.Name}」を削除しますか？\n\n" +
            (members > 0 ? $"中の {members} 件は未分類に移動します。アカウント自体は削除されません。"
                         : "空のグループです。"),
            "OTP Manager", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if(answer != DialogResult.Yes) return;

        // The members keep their place in the list; they simply stop being under a heading.
        foreach(var account in _store.Accounts.Where(a => a.Group == group.Name)) account.Group = "";
        _store.Order.Remove(ListOrder.ForGroup(group.Name));
        _store.Groups.Remove(group);
        Persist();
    }

    private void MoveGroup(AccountGroup group, int delta)
    {
        var order = _store.Order;
        var blocks = Blocks();
        var at = blocks.FindIndex(b => ListOrder.IsGroup(order[b.Start]) && ListOrder.GroupName(order[b.Start]) == group.Name);
        var to = at + delta;
        if(at < 0 || to < 0 || to >= blocks.Count) return;

        var self = blocks[at];
        var other = blocks[to];
        var block = order.GetRange(self.Start, self.Length);
        order.RemoveRange(self.Start, self.Length);

        // Moving down lands after the block that was next; moving up lands where it started.
        var target = delta > 0 ? other.Start + other.Length - self.Length : other.Start;
        order.InsertRange(Math.Clamp(target, 0, order.Count), block);
        Persist();
    }

    // --- QR panel --------------------------------------------------------

    private void BuildQrPanel()
    {
        _qrCard.Controls.Add(_qrImage);
        _qrCard.Controls.Add(_qrTitle);
        _qrCard.Controls.Add(_qrNote);
        _qrCard.Padding = new Padding(14);
        _qrCard.Paint += (_, e) => ControlPaint.DrawBorder(e.Graphics, _qrCard.ClientRectangle,
            ListStyle.CardBorder, ButtonBorderStyle.Solid);

        _qrMenu.Items.Add("画像を保存(&S)...", null, (_, _) => { if(_qrAccount != null) SaveQr(_qrAccount); });
        _qrMenu.Items.Add("base64形式でコピー(&B)", null, (_, _) => { if(_qrAccount != null) CopyQrImage(_qrAccount); });
        _qrMenu.Items.Add("authURL形式でコピー(&U)", null, (_, _) => { if(_qrAccount != null) CopyUri(_qrAccount); });

        // The same menu on every part of the card, so a right click never lands on dead space.
        foreach(var control in new Control[] { _qrCard, _qrImage, _qrTitle, _qrNote }) control.ContextMenuStrip = _qrMenu;

        // The list stays live underneath, just faded; clicking it is what closes the panel.
        _list.MouseClick += (_, e) => { if(e.Button == MouseButtons.Left) CloseQr(); };

        var listMenu = new ContextMenuStrip();
        listMenu.Items.Add("グループを追加(&G)...", null, (_, _) => AddGroup());
        _list.ContextMenuStrip = listMenu;
        _empty.MouseClick += (_, e) => { if(e.Button == MouseButtons.Left) CloseQr(); };
        // A disabled toolbar passes its clicks up to the form, so catch them here too.
        MouseClick += (_, e) => { if(e.Button == MouseButtons.Left) CloseQr(); };

        Controls.Add(_qrCard);
        Resize += (_, _) =>
        {
            if(_qrCard.Visible) LayoutQrCard();
            if(_toast.Visible) PlaceToast();
        };
    }

    private void LayoutQrCard()
    {
        var side = Math.Max(200, Math.Min(ClientSize.Width - 40, ClientSize.Height - 40));
        _qrCard.SetBounds((ClientSize.Width - side) / 2, (ClientSize.Height - side) / 2, side, side);
    }

    private void ShowQr(Account account)
    {
        _qrAccount = account;
        _qrTitle.Text = account.Title;

        SetDimmed(true);
        _qrCard.Visible = true;
        LayoutQrCard();
        _qrCard.BringToFront();

        var side = Math.Max(160, Math.Min(_qrImage.ClientSize.Width, _qrImage.ClientSize.Height));
        var next = QrCode.Encode(account.ToUri(), side);
        _qrImage.Image = next;
        _qrBitmap?.Dispose();
        _qrBitmap = next;
    }

    private void CloseQr()
    {
        if(!_qrCard.Visible) return;
        _qrCard.Visible = false;
        SetDimmed(false);
        _qrAccount = null;
        _qrImage.Image = null;
        _qrBitmap?.Dispose();
        _qrBitmap = null;
    }

    /// <summary>Fades the list rather than covering it, so the codes underneath keep running.</summary>
    private void SetDimmed(bool dimmed)
    {
        foreach(var row in _rows) row.Dimmed = dimmed;
        foreach(var header in _headers) header.Dimmed = dimmed;
        _list.BackColor = dimmed ? ListStyle.DimBackground : ListStyle.ListBackground;
        _empty.ForeColor = dimmed ? ListStyle.Blend(ListStyle.SubtleText, ListStyle.DimBackground, 0.6f) : ListStyle.SubtleText;
        _toolbar.Enabled = !dimmed;
    }

    private void SaveQr(Account account)
    {
        using var dialog = new SaveFileDialog
        {
            Title = "QRコードを保存",
            Filter = "PNG画像|*.png",
            FileName = SafeFileName(account) + ".png",
        };
        if(dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            using var image = QrCode.Encode(account.ToUri(), 512);
            image.Save(dialog.FileName, System.Drawing.Imaging.ImageFormat.Png);
            Say($"{account.Title} のQRコードを保存しました。");
        }
        catch(Exception ex)
        {
            Warn($"保存できませんでした。\n\n{ex.Message}");
        }
    }

    private void CopyQrImage(Account account)
    {
        try
        {
            using var image = QrCode.Encode(account.ToUri(), 512);
            using var stream = new MemoryStream();
            image.Save(stream, System.Drawing.Imaging.ImageFormat.Png);

            // A data: URI rather than bare base64, so this app can read its own output straight back.
            Clipboard.SetText("data:image/png;base64," + Convert.ToBase64String(stream.ToArray()));
            Say($"{account.Title} のQRコードをbase64でコピーしました。");
        }
        catch(Exception ex)
        {
            Warn($"コピーできませんでした。\n\n{ex.Message}");
        }
    }

    private void CopyUri(Account account)
    {
        try
        {
            Clipboard.SetText(account.ToUri());
            Say($"{account.Title} のauthURLをコピーしました。");
        }
        catch(Exception ex)
        {
            Warn($"コピーできませんでした。\n\n{ex.Message}");
        }
    }

    private static string SafeFileName(Account account)
    {
        var name = account.Title;
        foreach(var bad in Path.GetInvalidFileNameChars()) name = name.Replace(bad, (char)45);
        return name;
    }

    // --- QR import -------------------------------------------------------

    private void ImportFromFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "QRコードの画像を選択",
            Filter = "画像ファイル|*.png;*.jpg;*.jpeg;*.bmp;*.gif|すべてのファイル|*.*",
        };
        if(dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            using var bitmap = new Bitmap(dialog.FileName);
            ImportImage(bitmap);
        }
        catch(Exception ex)
        {
            Warn($"画像を読み込めませんでした。\n\n{ex.Message}");
        }
    }

    private void ImportFromClipboard()
    {
        // People copy the otpauth URI itself as often as they copy a picture of it.
        if(Clipboard.ContainsText())
        {
            ImportText(Clipboard.GetText());
            return;
        }
        if(!Clipboard.ContainsImage())
        {
            Warn("クリップボードに画像もテキストもありません。");
            return;
        }
        using var image = Clipboard.GetImage();
        if(image == null) { Warn("クリップボードの画像を取得できませんでした。"); return; }
        using var bitmap = new Bitmap(image);
        ImportImage(bitmap);
    }

    private void ImportImage(Bitmap bitmap)
    {
        string? text;
        try
        {
            text = QrCode.Decode(bitmap);
        }
        catch(Exception ex)
        {
            Warn($"QRコードの解析に失敗しました。\n\n{ex.Message}");
            return;
        }
        if(text == null)
        {
            Warn("QRコードを読み取れませんでした。\n\n画像が小さい、ぼやけている、傾きが大きい場合は失敗します。");
            return;
        }
        ImportText(text);
    }

    private void ImportText(string text)
    {
        text = text.Trim();

        // A data: URI is an image that arrived as text - unwrap it and read it as a picture.
        if(QrCode.TryDecodeDataUri(text, out var embedded))
        {
            using(embedded) ImportImage(embedded);
            return;
        }

        if(OtpMigration.IsMigrationUri(text)) { ImportMigration(text); return; }

        if(!Account.TryParseUri(text, out var account, out var error))
        {
            Warn($"OTPの情報として解釈できませんでした。\n\n{error}");
            return;
        }
        if(IsDuplicate(account)) { Warn($"「{account.Title}」は既に登録されています。"); return; }

        _store.Accounts.Add(account);
        Persist();
        Say($"{account.Title} を追加しました。");
    }

    private void ImportMigration(string text)
    {
        OtpMigration.Result result;
        try
        {
            result = OtpMigration.Parse(text);
        }
        catch(Exception ex)
        {
            Warn($"エクスポートデータを解釈できませんでした。\n\n{ex.Message}");
            return;
        }

        var fresh = result.Accounts.Where(a => !IsDuplicate(a)).ToList();
        var duplicates = result.Accounts.Count - fresh.Count;
        if(fresh.Count == 0)
        {
            Warn(duplicates > 0 ? "読み取れた分はすべて登録済みでした。" : "追加できるアカウントがありませんでした。");
            return;
        }

        var lines = string.Join("\n", fresh.Select(a => "・" + a.Title));
        var notes = "";
        if(duplicates > 0) notes += $"\n\n登録済みの {duplicates} 件は除きました。";
        if(result.SkippedHotp > 0) notes += $"\n\nHOTP形式の {result.SkippedHotp} 件は非対応のため取り込めません。";
        if(result.BatchCount > 1) notes += $"\n\n全 {result.BatchCount} 枚中 {result.Batch} 枚目です。残りのQRコードも読み取ってください。";

        var answer = MessageBox.Show(this, $"次の {fresh.Count} 件を追加します。\n\n{lines}{notes}",
            "OTP Manager", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
        if(answer != DialogResult.OK) return;

        _store.Accounts.AddRange(fresh);
        Persist();
        Say($"{fresh.Count} 件を追加しました。");
    }

    private bool IsDuplicate(Account candidate) =>
        _store.Accounts.Any(a => a.Secret.Equals(candidate.Secret, StringComparison.OrdinalIgnoreCase));

    private void Warn(string message) =>
        MessageBox.Show(this, message, "OTP Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    // --- drag reordering -------------------------------------------------

    /// <summary>Everything drawn in the list, top to bottom - group headings included.</summary>
    private List<Control> VisualItems() => _items;

    private static void SetGhost(Control item, bool ghost)
    {
        if(item is AccountRow row) row.Ghost = ghost;
        else if(item is GroupHeader header) header.Ghost = ghost;
    }

    private void BeginDrag(Control item, Point screenPoint)
    {
        _dragItem = item;
        SetGhost(item, true);
        _insertion.Visible = true;
        UpdateDrag(screenPoint);
    }

    private void UpdateDrag(Point screenPoint)
    {
        if(_dragItem == null) return;
        var point = _list.PointToClient(screenPoint);

        // Nudge the view when the pointer reaches an edge, so a long list stays reachable mid-drag.
        const int margin = 28;
        if(point.Y < margin) ScrollBy(-24);
        else if(point.Y > _list.ClientSize.Height - margin) ScrollBy(24);

        var items = VisualItems();
        var drop = ComputeDrop(point, items);
        _dropIndex = drop.Index;
        _dropGroup = drop.Group;

        // Only a row can join a group; dragging a heading always lands between sections.
        var joining = _dragItem is AccountRow && _dropGroup.Length > 0;
        HighlightGroup(joining ? _dropGroup : "");

        var y = _dropIndex < items.Count ? items[_dropIndex].Top
              : items.Count > 0 ? items[^1].Bottom
              : 0;

        // Landing at the end of a group would otherwise put the line on the group's bottom edge,
        // which reads as "below the group". Move it into the strip inside the outline instead.
        if(joining && _dropIndex > 0)
        {
            var previous = items[_dropIndex - 1];
            var closes = previous switch
            {
                AccountRow last => last.LastInGroup && last.Account.Group == _dropGroup,
                GroupHeader header => header.ClosesOutline && header.Group.Name == _dropGroup,
                _ => false,
            };
            if(closes) y = previous.Bottom - ListStyle.SectionGap - ListStyle.GroupPadding / 2 - 1;
        }
        var inset = joining ? ListStyle.GroupMargin + ListStyle.GroupPadding : ListStyle.CardMargin;
        _insertion.SetBounds(inset, Math.Max(0, y - 1), Math.Max(0, _list.ClientSize.Width - inset * 2), 2);
        _insertion.BringToFront();
    }

    /// <summary>
    /// Turns a pointer position into "where in the list" plus "which group, if any".
    /// <para>
    /// The two are separate on purpose. Reading the group from whatever sits above the insertion
    /// line makes the gap between one group and the next unreachable, because that gap is also the
    /// end of the group above. Here the item the pointer is actually over decides: its lower half
    /// means inside it, its upper half means before it - and the upper half of a heading, or of an
    /// ungrouped row, is exactly the slot between two sections.
    /// </para>
    /// </summary>
    private static (int Index, string Group) ComputeDrop(Point point, List<Control> items)
    {
        for(var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if(point.Y >= item.Bottom) continue;

            var middle = item.Top + item.Height / 2;
            if(item is GroupHeader header) return point.Y < middle ? (i, "") : (i + 1, header.Group.Name);
            if(item is AccountRow row) return point.Y < middle ? (i, row.Account.Group) : (i + 1, row.Account.Group);
        }
        return (items.Count, "");
    }

    private void HighlightGroup(string group)
    {
        foreach(var header in _headers) header.Highlight = group.Length > 0 && header.Group.Name == group;
        foreach(var row in _rows) row.Highlight = group.Length > 0 && row.Account.Group == group;
    }

    private void ScrollBy(int delta) => _list.AutoScrollPosition = new Point(0, -_list.AutoScrollPosition.Y + delta);

    private void EndDrag()
    {
        var item = _dragItem;
        _dragItem = null;
        _insertion.Visible = false;
        if(item == null) return;
        SetGhost(item, false);
        HighlightGroup("");
        if(_dropIndex < 0) return;

        if(item is AccountRow row) DropAccount(row);
        else if(item is GroupHeader header) DropGroup(header);
    }

    /// <summary>Moves one account to the slot and group that <see cref="ComputeDrop"/> worked out.</summary>
    private void DropAccount(AccountRow row)
    {
        var items = VisualItems();
        var group = _dropGroup;

        // The token to insert after: whatever is directly above the line, ignoring the dragged row.
        string? anchorToken = null;
        for(var i = Math.Min(_dropIndex, items.Count) - 1; i >= 0; i--)
        {
            if(ReferenceEquals(items[i], row)) continue;
            anchorToken = items[i] switch
            {
                GroupHeader header => ListOrder.ForGroup(header.Group.Name),
                AccountRow other => ListOrder.ForAccount(other.Account),
                _ => null,
            };
            if(anchorToken != null) break;
        }

        var account = row.Account;
        var order = _store.Order;
        order.Remove(ListOrder.ForAccount(account));
        account.Group = group;

        var at = anchorToken != null ? order.IndexOf(anchorToken) + 1
               : group.Length > 0 ? order.IndexOf(ListOrder.ForGroup(group)) + 1
               : 0;
        order.Insert(Math.Clamp(at, 0, order.Count), ListOrder.ForAccount(account));
        Persist();
    }

    /// <summary>Moves a heading and its members as one block, never landing inside another group.</summary>
    private void DropGroup(GroupHeader header)
    {
        var items = VisualItems();
        var order = _store.Order;

        var start = order.IndexOf(ListOrder.ForGroup(header.Group.Name));
        if(start < 0) { Rebuild(); return; }

        var members = _store.Accounts.Where(a => a.Group == header.Group.Name)
            .Select(ListOrder.ForAccount).ToHashSet(StringComparer.Ordinal);
        var length = 1;
        while(start + length < order.Count && members.Contains(order[start + length])) length++;

        var block = order.GetRange(start, length);

        // Land on a boundary: a drop onto a grouped row means "before that whole group".
        var target = order.Count;
        if(_dropIndex < items.Count)
        {
            var token = items[_dropIndex] switch
            {
                GroupHeader other => ListOrder.ForGroup(other.Group.Name),
                AccountRow row => row.Account.Group.Length > 0 ? ListOrder.ForGroup(row.Account.Group)
                                                              : ListOrder.ForAccount(row.Account),
                _ => null,
            };
            if(token != null)
            {
                var at = order.IndexOf(token);
                if(at >= 0) target = at;
            }
        }

        order.RemoveRange(start, length);
        if(target > start) target -= length;
        order.InsertRange(Math.Clamp(target, 0, order.Count), block);
        Persist();
    }

    private void Persist()
    {
        try
        {
            _store.Repair();
            _store.Save();
        }
        catch(Exception ex)
        {
            MessageBox.Show(this, $"保存に失敗しました。\n\n{ex.Message}", "OTP Manager",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        Rebuild();
    }

    protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified) =>
        base.SetBoundsCore(x, y, FixedWidth, height, specified);

    public void ShowFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void HideToTray()
    {
        CloseQr();
        SaveWindowBounds();
        Hide();
    }

    // --- window geometry -------------------------------------------------

    private sealed record WindowBounds(int X, int Y, int Width, int Height);

    private static string BoundsPath => Path.Combine(AccountStore.Directory, "window.json");

    private void LoadWindowBounds()
    {
        try
        {
            if(!File.Exists(BoundsPath)) return;
            var saved = JsonSerializer.Deserialize<WindowBounds>(File.ReadAllText(BoundsPath));
            if(saved == null || saved.Height < MinimumSize.Height) return;

            // Ignore a saved position that no longer lands on a connected monitor. The width is not
            // restored, because it is not the user's to change.
            var rect = new Rectangle(saved.X, saved.Y, FixedWidth, saved.Height);
            if(!Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(rect))) return;

            StartPosition = FormStartPosition.Manual;
            Bounds = rect;
        }
        catch(Exception)
        {
            // Geometry is a convenience; never block startup on it.
        }
    }

    private void SaveWindowBounds()
    {
        try
        {
            if(WindowState != FormWindowState.Normal) return;
            System.IO.Directory.CreateDirectory(AccountStore.Directory);
            File.WriteAllText(BoundsPath, JsonSerializer.Serialize(new WindowBounds(Left, Top, Width, Height)));
        }
        catch(Exception)
        {
        }
    }

    public void PrepareForExit() => SaveWindowBounds();

    protected override void Dispose(bool disposing)
    {
        if(disposing)
        {
            Theme.Changed -= ApplyTheme;
            Background.Changed -= RepaintList;
            _timer.Dispose();
            _qrBitmap?.Dispose();
        }
        base.Dispose(disposing);
    }
}
