using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace OtpManager;

/// <summary>One account line: title, current code, and the seconds left on it.</summary>
internal sealed class AccountRow : Control
{
    private readonly Font _titleFont;
    private readonly Font _codeFont;
    private readonly Font _iconFont;
    private readonly ContextMenuStrip _rowMenu = new();
    private readonly ContextMenuStrip _iconMenu = new();

    private bool _hover;
    private string _code = "";
    private float _remaining = 1f;
    private long _codeSecond = -1;
    private float _pulse = 1f;
    private bool _broken;

    private Point? _pressedAt;
    private bool _dragging;
    private bool _swallowClick;
    private bool _ghost;
    private bool _iconHover;
    private bool _dimmed;
    private bool _highlight;
    private bool _lastInGroup;

    /// <summary>Whether this row sits inside a group's outline, and whether it closes it.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool InGroup { get; set; }

    /// <summary>
    /// The final row of a group is taller than the rest. The extra strip sits inside the outline
    /// and gives the insertion line somewhere to be drawn that still reads as "in this group".
    /// </summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool LastInGroup
    {
        get => _lastInGroup;
        set
        {
            if(_lastInGroup == value) return;
            _lastInGroup = value;
            Height = BaseHeight + (value ? ListStyle.GroupPadding + ListStyle.SectionGap : 0);
            Invalidate();
        }
    }

    /// <summary>Drawn in blue while a drag is about to drop into this row's group.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Highlight
    {
        get => _highlight;
        set { if(_highlight == value) return; _highlight = value; Invalidate(); }
    }

    public Account Account { get; }

    public event EventHandler? CopyRequested;
    /// <summary>Raised when the row is clicked while dimmed, i.e. something else is in front of it.</summary>
    public event EventHandler? DismissRequested;
    public event EventHandler? EditRequested;
    public event EventHandler? DeleteRequested;
    public event EventHandler? QrRequested;
    public event EventHandler? SaveQrRequested;
    public event EventHandler? CopyQrImageRequested;
    public event EventHandler? CopyUriRequested;
    public event EventHandler<int>? MoveRequested;

    /// <summary>Raised once the pointer has moved far enough to count as a drag rather than a click.</summary>
    public event EventHandler<Point>? DragBegan;
    public event EventHandler<Point>? DragMoved;
    public event EventHandler? DragEnded;

    /// <summary>Fades the row while the QR panel is covering the list, keeping it live underneath.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Dimmed
    {
        get => _dimmed;
        set { if(_dimmed == value) return; _dimmed = value; _hover = false; _iconHover = false; Invalidate(); }
    }

    /// <summary>Dims the row while it is the one being dragged.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Ghost
    {
        get => _ghost;
        set { if(_ghost == value) return; _ghost = value; Invalidate(); }
    }

    public AccountRow(Account account)
    {
        Account = account;
        Height = BaseHeight;
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        ResizeRedraw = true;
        TabStop = true;

        _titleFont = new Font(Font.FontFamily, 9f);
        _codeFont = new Font("Consolas", 22f, FontStyle.Bold);
        _iconFont = MaterialSymbols.Get(20f);

        _iconMenu.Items.Add("画像を保存(&S)...", null, (_, _) => SaveQrRequested?.Invoke(this, EventArgs.Empty));
        _iconMenu.Items.Add("base64形式でコピー(&B)", null, (_, _) => CopyQrImageRequested?.Invoke(this, EventArgs.Empty));
        _iconMenu.Items.Add("authURL形式でコピー(&U)", null, (_, _) => CopyUriRequested?.Invoke(this, EventArgs.Empty));

        _rowMenu.Items.Add("コードをコピー(&C)", null, (_, _) => CopyRequested?.Invoke(this, EventArgs.Empty));
        _rowMenu.Items.Add(new ToolStripSeparator());
        _rowMenu.Items.Add("編集(&E)...", null, (_, _) => EditRequested?.Invoke(this, EventArgs.Empty));
        _rowMenu.Items.Add("QRコードを表示(&Q)...", null, (_, _) => QrRequested?.Invoke(this, EventArgs.Empty));
        _rowMenu.Items.Add("削除(&D)...", null, (_, _) => DeleteRequested?.Invoke(this, EventArgs.Empty));
        _rowMenu.Items.Add(new ToolStripSeparator());
        _rowMenu.Items.Add("上へ移動(&U)", null, (_, _) => MoveRequested?.Invoke(this, -1));
        _rowMenu.Items.Add("下へ移動(&N)", null, (_, _) => MoveRequested?.Invoke(this, 1));
    }

    /// <summary>Refreshes the displayed code. Called on a timer by the form.</summary>
    public void Tick(long unixMilliseconds)
    {
        // The bar is repainted many times a second; the code itself only has to be worked out once
        // per second, so the HMAC is not run on every frame.
        var second = unixMilliseconds / 1000;
        var codeChanged = false;
        if(second != _codeSecond)
        {
            _codeSecond = second;
            string code;
            var broken = false;
            try
            {
                code = Account.Code(second);
            }
            catch(Exception)
            {
                code = "エラー";
                broken = true;
            }
            codeChanged = code != _code || broken != _broken;
            _code = code;
            _broken = broken;
        }

        var remaining = Totp.RemainingFraction(unixMilliseconds, Account.Period);

        // One flash per second while the code is about to expire, as a smooth fade rather than a blink.
        var wasExpiring = Expiring;
        _pulse = 0.5f + 0.5f * (float)Math.Sin(unixMilliseconds % 1000 / 1000.0 * Math.PI * 2.0);
        var moved = Math.Abs(remaining - _remaining) > 0.0005f;
        _remaining = remaining;

        // Between code changes only the bar moves, so repaint just that strip - unless the digits
        // are flashing, in which case the code area has to come along too.
        if(codeChanged) Invalidate();
        else
        {
            if(moved) Invalidate(BarRect);
            if(Expiring || wasExpiring) Invalidate(CodeRect);
        }
    }

    internal const int BaseHeight = 76;

    private Rectangle Card => ListStyle.CardBounds(Width, BaseHeight, InGroup);

    /// <summary>The progress bar itself. One definition, so painting and invalidating cannot drift.</summary>
    private Rectangle ProgressBounds
    {
        get
        {
            var card = Card;
            return new Rectangle(card.Left + 10, card.Bottom - 8, card.Width - 20, 3);
        }
    }

    /// <summary>The strip repainted between code changes: the bar plus a pixel of slack.</summary>
    private Rectangle BarRect => Rectangle.Inflate(ProgressBounds, 2, 2);

    /// <summary>The area the code itself occupies, repainted on its own while the digits pulse.</summary>
    private Rectangle CodeRect => new(Card.Left, 20, Card.Width, 44);

    /// <summary>True in the last few seconds of a code's life, when the digits start flashing.</summary>
    private bool Expiring => !_broken && _remaining * Account.Period <= 5f;

    public string CurrentCode => _broken ? "" : _code;

    /// <summary>Hit area of the QR button in the top right corner of the row.</summary>
    private Rectangle IconRect => new(Card.Right - 34, Card.Top + 4, 28, 28);

    private static string Group(string code) =>
        code.Length >= 6 && !code.Any(char.IsLetter)
            ? code[..(code.Length / 2)] + " " + code[(code.Length / 2)..]
            : code;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Background.PaintBehind(g, this, BackColorFor());

        var card = Card;
        if(InGroup) PaintGroupOutline(g);
        PaintCard(g, card);

        var accent = _broken ? ListStyle.CodeBroken : Expiring ? ListStyle.CodeWarn : ListStyle.CodeAccent;
        var titleColor = ListStyle.Title;
        if(_ghost)
        {
            // Washed out, so the insertion line is what the eye follows during a drag.
            accent = ListStyle.Blend(accent, ListStyle.CardGhost, 0.6f);
            titleColor = ListStyle.Blend(titleColor, ListStyle.CardGhost, 0.6f);
        }
        if(_dimmed)
        {
            accent = ListStyle.Blend(accent, DimBackground, 0.82f);
            titleColor = ListStyle.Blend(titleColor, DimBackground, 0.82f);
        }

        var left = card.Left + 10;
        var textWidth = card.Width - 20;

        TextRenderer.DrawText(g, Account.Title, _titleFont, new Rectangle(left, card.Top + 6, textWidth - 30, 18),
            titleColor, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

        var codeColor = Expiring ? ListStyle.Blend(accent, CardFillFor(), (1f - _pulse) * 0.75f) : accent;
        TextRenderer.DrawText(g, Group(_code), _codeFont, new Point(left - 2, card.Top + 24), codeColor,
            TextFormatFlags.NoPrefix);

        PaintProgress(g, accent);
        PaintIcon(g);
    }

    private void PaintCard(Graphics g, Rectangle card)
    {
        var fillColour = CardFillFor();
        if(Background.HasImage) fillColour = Color.FromArgb(226, fillColour);

        using(var fill = new SolidBrush(fillColour)) g.FillRectangle(fill, card);
        using var border = new Pen(_dimmed ? ListStyle.Blend(ListStyle.CardBorder, DimBackground, 0.72f)
                                           : ListStyle.CardBorder);
        g.DrawRectangle(border, card.Left, card.Top, card.Width - 1, card.Height - 1);
    }

    /// <summary>The sides of the enclosing group, and its bottom edge when this is the last member.</summary>
    private void PaintGroupOutline(Graphics g)
    {
        var left = ListStyle.GroupMargin;
        var right = Width - ListStyle.GroupMargin - 1;

        // The last row carries the gap that separates this group from whatever follows it.
        var bottom = Height - (LastInGroup ? ListStyle.SectionGap : 0);

        var groupColour = GroupFillFor();
        if(Background.HasImage) groupColour = Color.FromArgb(200, groupColour);

        using(var fill = new SolidBrush(groupColour))
            g.FillRectangle(fill, left, 0, right - left + 1, bottom);

        using var pen = new Pen(_highlight ? ListStyle.Highlight
                              : _dimmed ? ListStyle.Blend(ListStyle.GroupBorder, DimBackground, 0.72f)
                              : ListStyle.GroupBorder);
        g.DrawLine(pen, left, 0, left, bottom);
        g.DrawLine(pen, right, 0, right, bottom);
        if(LastInGroup) g.DrawLine(pen, left, bottom - 1, right, bottom - 1);
    }

    private void PaintProgress(Graphics g, Color accent)
    {
        var bar = ProgressBounds;

        var track = _dimmed ? ListStyle.Blend(ListStyle.ProgressTrack, DimBackground, 0.82f)
                            : ListStyle.ProgressTrack;
        using(var brush = new SolidBrush(track)) g.FillRectangle(brush, bar);

        if(_broken) return;
        var ratio = Math.Clamp(_remaining, 0f, 1f);
        using var fill = new SolidBrush(accent);
        g.FillRectangle(fill, bar.Left, bar.Top, bar.Width * ratio, bar.Height);
    }

    private void PaintIcon(Graphics g)
    {
        if(!MaterialSymbols.Available) return;

        var icon = IconRect;
        if(_iconHover && !_ghost && !_dimmed)
        {
            using var highlight = new SolidBrush(ListStyle.IconHoverFill);
            g.FillRectangle(highlight, icon);
        }

        // GDI cannot see the privately loaded font, so the glyph has to go through GDI+.
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var glyph = new SolidBrush(_dimmed ? ListStyle.Blend(ListStyle.Icon, DimBackground, 0.82f)
                                       : _ghost ? ListStyle.Blend(ListStyle.Icon, ListStyle.CardGhost, 0.5f)
                                       : _iconHover ? ListStyle.IconActive
                                       : ListStyle.Icon);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(MaterialSymbols.QrCode, _iconFont, glyph, icon, format);
    }

    private Color CardFillFor() => _dimmed ? ListStyle.Blend(ListStyle.CardFill, DimBackground, 0.55f)
                                 : _ghost ? ListStyle.CardGhost
                                 : _hover ? ListStyle.CardHover
                                 : ListStyle.CardFill;

    private Color GroupFillFor() => _highlight ? ListStyle.HighlightFill
                                  : _dimmed ? ListStyle.Blend(ListStyle.GroupFill, DimBackground, 0.72f)
                                  : ListStyle.GroupFill;

    private Color BackColorFor() => _dimmed ? DimBackground : ListStyle.ListBackground;

    /// <summary>Colour the list fades towards while the QR panel is open.</summary>
    internal static Color DimBackground => ListStyle.DimBackground;

    protected override void OnMouseEnter(EventArgs e)
    {
        if(!_dimmed) { _hover = true; Invalidate(); }
        base.OnMouseEnter(e);
    }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _iconHover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        // Pressing the icon must not arm a drag, or the button would be hard to hit.
        if(!_dimmed && e.Button == MouseButtons.Left && !IconRect.Contains(e.Location)) _pressedAt = e.Location;
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if(_dimmed) { base.OnMouseMove(e); return; }

        var overIcon = IconRect.Contains(e.Location);
        if(overIcon != _iconHover) { _iconHover = overIcon; Invalidate(IconRect); }

        if(_pressedAt is Point start)
        {
            if(!_dragging)
            {
                // The system drag threshold keeps an unsteady click from turning into a reorder.
                var threshold = SystemInformation.DragSize;
                if(Math.Abs(e.X - start.X) > threshold.Width / 2 || Math.Abs(e.Y - start.Y) > threshold.Height / 2)
                {
                    _dragging = true;
                    Cursor = Cursors.SizeNS;
                    DragBegan?.Invoke(this, PointToScreen(e.Location));
                }
            }
            else
            {
                DragMoved?.Invoke(this, PointToScreen(e.Location));
            }
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if(_dimmed) { base.OnMouseUp(e); return; }
        if(e.Button == MouseButtons.Right)
        {
            // Two menus share the row: the icon owns its corner, the rest belongs to the account.
            var menu = IconRect.Contains(e.Location) ? _iconMenu : _rowMenu;
            menu.Show(this, e.Location);
        }
        if(_dragging)
        {
            _dragging = false;
            _swallowClick = true;
            Cursor = Cursors.Hand;
            DragEnded?.Invoke(this, EventArgs.Empty);
        }
        _pressedAt = null;
        base.OnMouseUp(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        if(_swallowClick)
        {
            // A drag ends with a click message too; copying here would be surprising.
            _swallowClick = false;
            return;
        }
        if(_dimmed)
        {
            if(e.Button == MouseButtons.Left) DismissRequested?.Invoke(this, EventArgs.Empty);
            return;
        }
        if(e.Button == MouseButtons.Left)
        {
            if(IconRect.Contains(e.Location)) QrRequested?.Invoke(this, EventArgs.Empty);
            else CopyRequested?.Invoke(this, EventArgs.Empty);
        }
        base.OnMouseClick(e);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        if(!_dimmed && e.Button == MouseButtons.Left && !IconRect.Contains(e.Location)) EditRequested?.Invoke(this, EventArgs.Empty);
        base.OnMouseDoubleClick(e);
    }

    protected override void Dispose(bool disposing)
    {
        if(disposing)
        {
            _titleFont.Dispose();
            _codeFont.Dispose();
            _rowMenu.Dispose();
            _iconMenu.Dispose();
        }
        base.Dispose(disposing);
    }
}
