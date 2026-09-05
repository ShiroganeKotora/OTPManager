using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace OtpManager;

/// <summary>A heading band in the list. Clicking it folds the group away.</summary>
internal sealed class GroupHeader : Control
{
    private readonly Font _nameFont;
    private readonly ContextMenuStrip _menu = new();
    private bool _hover;
    private bool _dimmed;
    private bool _highlight;
    private bool _closesOutline;
    private bool _ghost;
    private Point? _pressedAt;
    private bool _dragging;
    private bool _swallowClick;

    internal const int BaseHeight = 28;

    public AccountGroup Group { get; }
    public int Count { get; }

    /// <summary>True when no member rows follow, so the heading has to close the outline itself.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ClosesOutline
    {
        get => _closesOutline;
        set
        {
            if(_closesOutline == value) return;
            _closesOutline = value;
            // With no members below, the heading has to provide the strip inside its own outline.
            Height = BaseHeight + ListStyle.SectionGap + (value ? ListStyle.GroupPadding + ListStyle.SectionGap : 0);
            Invalidate();
        }
    }

    /// <summary>Drawn in blue while a drag is about to drop into this group.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Highlight
    {
        get => _highlight;
        set { if(_highlight == value) return; _highlight = value; Invalidate(); }
    }

    public event EventHandler? ToggleRequested;
    public event EventHandler? RenameRequested;
    public event EventHandler? DeleteRequested;
    public event EventHandler<int>? MoveRequested;

    /// <summary>Dragging a heading carries the whole group with it.</summary>
    public event EventHandler<Point>? DragBegan;
    public event EventHandler<Point>? DragMoved;
    public event EventHandler? DragEnded;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Ghost
    {
        get => _ghost;
        set { if(_ghost == value) return; _ghost = value; Invalidate(); }
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Dimmed
    {
        get => _dimmed;
        set { if(_dimmed == value) return; _dimmed = value; _hover = false; Invalidate(); }
    }

    public GroupHeader(AccountGroup group, int count)
    {
        Group = group;
        Count = count;
        Height = BaseHeight + ListStyle.SectionGap;
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        ResizeRedraw = true;

        _nameFont = new Font(Font.FontFamily, 9f, FontStyle.Bold);

        _menu.Items.Add("名前を変更(&R)...", null, (_, _) => RenameRequested?.Invoke(this, EventArgs.Empty));
        _menu.Items.Add("グループを削除(&D)...", null, (_, _) => DeleteRequested?.Invoke(this, EventArgs.Empty));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("上へ移動(&U)", null, (_, _) => MoveRequested?.Invoke(this, -1));
        _menu.Items.Add("下へ移動(&N)", null, (_, _) => MoveRequested?.Invoke(this, 1));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Background.PaintBehind(g, this, _dimmed ? AccountRow.DimBackground : ListStyle.ListBackground);

        var left = ListStyle.GroupMargin;
        var right = Width - ListStyle.GroupMargin - 1;

        // The heading carries the gap that separates this group from whatever came before it, and
        // when nothing follows inside the group, the gap after it as well.
        var top = ListStyle.SectionGap;
        var bottom = ClosesOutline ? Height - ListStyle.SectionGap : Height;

        var band = _highlight ? ListStyle.HighlightBand
                 : _dimmed ? ListStyle.Blend(ListStyle.HeaderBand, AccountRow.DimBackground, 0.72f)
                 : _ghost ? ListStyle.Blend(ListStyle.HeaderBand, ListStyle.CardGhost, 0.6f)
                 : _hover ? ListStyle.HeaderBandHover
                 : ListStyle.HeaderBand;
        if(Background.HasImage) band = Color.FromArgb(216, band);
        using(var fill = new SolidBrush(band)) g.FillRectangle(fill, left, top + 1, right - left + 1, BaseHeight - 1);

        using(var pen = new Pen(_highlight ? ListStyle.Highlight
                              : _dimmed ? ListStyle.Blend(ListStyle.GroupBorder, AccountRow.DimBackground, 0.72f)
                              : ListStyle.GroupBorder))
        {
            g.DrawLine(pen, left, top + 1, right, top + 1);
            g.DrawLine(pen, left, top + 1, left, bottom);
            g.DrawLine(pen, right, top + 1, right, bottom);
            if(ClosesOutline)
            {
                // The strip below the band is inside the group, so it takes the group's fill.
                using(var fill = new SolidBrush(_highlight ? ListStyle.HighlightFill : ListStyle.GroupFill))
                    g.FillRectangle(fill, left + 1, top + BaseHeight, right - left - 1, bottom - top - BaseHeight - 1);
                g.DrawLine(pen, left, bottom - 1, right, bottom - 1);
            }
        }

        var text = _dimmed ? ListStyle.Blend(ListStyle.HeaderText, AccountRow.DimBackground, 0.75f)
                 : _ghost ? ListStyle.Blend(ListStyle.HeaderText, ListStyle.CardGhost, 0.6f)
                 : ListStyle.HeaderText;
        DrawChevron(g, text, top);

        TextRenderer.DrawText(g, Group.Name, _nameFont, new Rectangle(left + 24, top + 1, Width - left - 60, BaseHeight - 1),
            text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

        var count = Count.ToString();
        var size = TextRenderer.MeasureText(g, count, Font);
        TextRenderer.DrawText(g, count, Font, new Point(right - 10 - size.Width, top + (BaseHeight - size.Height) / 2 + 1),
            _dimmed ? ListStyle.Blend(ListStyle.HeaderCount, AccountRow.DimBackground, 0.75f) : ListStyle.HeaderCount,
            TextFormatFlags.NoPrefix);
    }

    /// <summary>A small triangle, drawn rather than taken from a font so no extra glyph is needed.</summary>
    private void DrawChevron(Graphics g, Color color, int top)
    {
        var cx = ListStyle.GroupMargin + 12f;
        var cy = top + BaseHeight / 2f;
        var r = 3.8f;
        var points = Group.Collapsed
            ? new[] { new PointF(cx - r + 1, cy - r - 1), new PointF(cx + r + 1, cy), new PointF(cx - r + 1, cy + r + 1) }
            : new[] { new PointF(cx - r - 1, cy - r + 1), new PointF(cx + r + 1, cy - r + 1), new PointF(cx, cy + r + 1) };
        using var brush = new SolidBrush(color);
        g.FillPolygon(brush, points);
    }

    protected override void OnMouseEnter(EventArgs e) { if(!_dimmed) { _hover = true; Invalidate(); } base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if(!_dimmed && e.Button == MouseButtons.Left) _pressedAt = e.Location;
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if(_pressedAt is Point start)
        {
            if(!_dragging)
            {
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
        if(_dragging)
        {
            _dragging = false;
            _swallowClick = true;
            Cursor = Cursors.Hand;
            DragEnded?.Invoke(this, EventArgs.Empty);
        }
        _pressedAt = null;
        if(!_dimmed && e.Button == MouseButtons.Right) _menu.Show(this, e.Location);
        base.OnMouseUp(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        if(_swallowClick)
        {
            // A drag ends with a click message too; folding the group here would be surprising.
            _swallowClick = false;
            return;
        }
        if(e.Button == MouseButtons.Left) ToggleRequested?.Invoke(this, EventArgs.Empty);
        base.OnMouseClick(e);
    }

    protected override void Dispose(bool disposing)
    {
        if(disposing) { _nameFont.Dispose(); _menu.Dispose(); }
        base.Dispose(disposing);
    }
}
