using System.ComponentModel;

namespace OtpManager;

/// <summary>
/// The row of registered pictures. Each tile is cropped to the shape of the list, so a tile is a
/// small copy of what that picture actually looks like behind the window rather than a view of the
/// whole file - the parts a portrait window never shows would only mislead.
/// </summary>
internal sealed class BackgroundStrip : Control
{
    private const int Arrow = 18;
    private const int Gap = 10;
    private const int Pad = 4;

    private Size _viewport = new(340, 800);
    private int _first;
    private int _hover = -1;

    public BackgroundStrip()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.UserPaint, true);
        ResizeRedraw = true;
        Height = 124;
    }

    /// <summary>Raised when a different picture is picked.</summary>
    public event Action<int>? Selected;

    /// <summary>The shape the tiles are cropped to - the list, not this strip.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Size Viewport
    {
        get => _viewport;
        set
        {
            if(value.Width <= 0 || value.Height <= 0) return;
            _viewport = value;
            Invalidate();
        }
    }

    private Size TileSize
    {
        get
        {
            var height = Math.Max(24, Height - Pad * 2);
            var width = Math.Max(12, (int)Math.Round(height * (double)_viewport.Width / _viewport.Height));
            return new Size(width, height);
        }
    }

    private int Capacity
    {
        get
        {
            var room = Width - (Arrow + Gap) * 2;
            var tile = TileSize.Width + Gap;
            return Math.Max(1, (room + Gap) / tile);
        }
    }

    private Rectangle TileBounds(int slot)
    {
        var tile = TileSize;
        return new Rectangle(Arrow + Gap + slot * (tile.Width + Gap), Pad, tile.Width, tile.Height);
    }

    private Rectangle LeftArrow => new(0, 0, Arrow, Height);
    private Rectangle RightArrow => new(Width - Arrow, 0, Arrow, Height);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;

        // Not BackColor: this control inherits Transparent from the page, and clearing to that
        // paints an undefined colour rather than letting the parent show through.
        g.Clear(ListStyle.DialogBackground);

        // An outline is what makes this read as one area rather than tiles floating on the page.
        using(var edge = new Pen(ListStyle.CardBorder))
            g.DrawRectangle(edge, 0, 0, Width - 1, Height - 1);

        var images = Background.Images;
        if(images.Count == 0)
        {
            TextRenderer.DrawText(g, "画像が登録されていません", Font, ClientRectangle, ListStyle.SubtleText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        _first = Math.Clamp(_first, 0, Math.Max(0, images.Count - Capacity));

        var tile = TileSize;
        var current = Background.CurrentIndex;

        for(var slot = 0; slot < Capacity && _first + slot < images.Count; slot++)
        {
            var index = _first + slot;
            var bounds = TileBounds(slot);

            var thumbnail = Background.Thumbnail(images[index], tile);
            if(thumbnail != null) g.DrawImage(thumbnail, bounds);
            else using(var empty = new SolidBrush(ListStyle.ProgressTrack)) g.FillRectangle(empty, bounds);

            if(index == current)
            {
                using var pen = new Pen(ListStyle.Highlight, 2f);
                g.DrawRectangle(pen, bounds.Left, bounds.Top, bounds.Width - 1, bounds.Height - 1);
            }
            else
            {
                using var pen = new Pen(index == _hover ? ListStyle.IconActive : ListStyle.CardBorder);
                g.DrawRectangle(pen, bounds.Left, bounds.Top, bounds.Width - 1, bounds.Height - 1);
            }
        }

        PaintArrow(g, LeftArrow, "‹", _first > 0);
        PaintArrow(g, RightArrow, "›", _first + Capacity < images.Count);
    }

    private void PaintArrow(Graphics g, Rectangle bounds, string glyph, bool live)
    {
        using var font = new Font(Font.FontFamily, 16f, FontStyle.Bold);

        // Faded against the page's own colour. Blending against BackColor would mix with the
        // Transparent this control inherits, which is not a colour at all.
        var ink = live ? ListStyle.DialogText
                       : ListStyle.Blend(ListStyle.SubtleText, ListStyle.DialogBackground, 0.6f);

        TextRenderer.DrawText(g, glyph, font, bounds, ink,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private int IndexAt(Point point)
    {
        var images = Background.Images;
        for(var slot = 0; slot < Capacity && _first + slot < images.Count; slot++)
            if(TileBounds(slot).Contains(point)) return _first + slot;

        return -1;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        var images = Background.Images;

        if(LeftArrow.Contains(e.Location))
        {
            if(_first > 0) { _first--; Invalidate(); }
        }
        else if(RightArrow.Contains(e.Location))
        {
            if(_first + Capacity < images.Count) { _first++; Invalidate(); }
        }
        else
        {
            var index = IndexAt(e.Location);
            if(index >= 0) Selected?.Invoke(index);
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var index = IndexAt(e.Location);
        if(index != _hover)
        {
            _hover = index;
            Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = -1;
        Cursor = Cursors.Default;
        Invalidate();
        base.OnMouseLeave(e);
    }

    /// <summary>Brings the picture on screen into view, after it was changed from elsewhere.</summary>
    public void ScrollTo(int index)
    {
        if(index < 0) return;
        if(index < _first) _first = index;
        else if(index >= _first + Capacity) _first = index - Capacity + 1;
        Invalidate();
    }
}
