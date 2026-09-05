using System.Drawing.Drawing2D;

namespace OtpManager;

/// <summary>
/// Shows the whole picture with a frame over it marking the part that will be visible behind the
/// list. Dragging the frame is how the user says which part of the picture matters; everything
/// outside it is what the window has no room for.
/// </summary>
internal sealed class BackgroundPicker : Control
{
    private Size _viewport;
    private PointF _focus;
    private Point? _dragFrom;
    private PointF _dragStartFocus;

    /// <summary>Raised while the frame is being moved, with the point as a fraction of the picture.</summary>
    public event Action<PointF>? FocusChanged;

    public BackgroundPicker()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        ResizeRedraw = true;
        Cursor = Cursors.SizeAll;
    }

    /// <summary>The shape of the area the picture has to cover - the list, not this preview.</summary>
    public void Configure(Size viewport, PointF focus)
    {
        _viewport = viewport;
        _focus = focus;
        Invalidate();
    }

    /// <summary>Where the picture sits inside this control when shown whole.</summary>
    private Rectangle Fitted
    {
        get
        {
            var source = Background.SourceSize;
            if(source.IsEmpty || ClientSize.Width <= 0 || ClientSize.Height <= 0) return Rectangle.Empty;

            var scale = Math.Min((float)ClientSize.Width / source.Width, (float)ClientSize.Height / source.Height);
            var width = (int)Math.Round(source.Width * scale);
            var height = (int)Math.Round(source.Height * scale);
            return new Rectangle((ClientSize.Width - width) / 2, (ClientSize.Height - height) / 2, width, height);
        }
    }

    /// <summary>The visible part of the picture, in the coordinates of this preview.</summary>
    private Rectangle Frame
    {
        get
        {
            var fitted = Fitted;
            var source = Background.SourceSize;
            if(fitted.IsEmpty || _viewport.Width <= 0 || _viewport.Height <= 0) return Rectangle.Empty;

            // How much of the picture the window can show, once the picture is scaled up to cover it.
            var cover = Math.Max((float)_viewport.Width / source.Width, (float)_viewport.Height / source.Height);
            var visible = new SizeF(Math.Min(_viewport.Width / cover, source.Width),
                                    Math.Min(_viewport.Height / cover, source.Height));

            var centre = new PointF(_focus.X * source.Width, _focus.Y * source.Height);
            var left = Math.Clamp(centre.X - visible.Width / 2, 0, source.Width - visible.Width);
            var top = Math.Clamp(centre.Y - visible.Height / 2, 0, source.Height - visible.Height);

            var preview = (float)fitted.Width / source.Width;
            return new Rectangle(
                fitted.Left + (int)Math.Round(left * preview),
                fitted.Top + (int)Math.Round(top * preview),
                Math.Max(1, (int)Math.Round(visible.Width * preview)),
                Math.Max(1, (int)Math.Round(visible.Height * preview)));
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(ListStyle.SidebarBackground);

        var fitted = Fitted;
        if(fitted.IsEmpty)
        {
            TextRenderer.DrawText(g, "画像が選ばれていません", Font, ClientRectangle, ListStyle.SubtleText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        Background.DrawSource(g, fitted);

        var frame = Frame;
        if(frame.IsEmpty) return;

        // Everything outside the frame is dimmed, so the chosen part reads as the one in focus.
        using(var shade = new SolidBrush(Color.FromArgb(140, 0, 0, 0)))
        {
            var region = new Region(fitted);
            region.Exclude(frame);
            g.FillRegion(shade, region);
            region.Dispose();
        }

        using var pen = new Pen(Color.White, 2);
        g.DrawRectangle(pen, frame.Left, frame.Top, frame.Width - 1, frame.Height - 1);
        using var inner = new Pen(Color.FromArgb(120, 0, 0, 0), 1);
        g.DrawRectangle(inner, frame.Left + 1, frame.Top + 1, frame.Width - 3, frame.Height - 3);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if(e.Button == MouseButtons.Left && !Fitted.IsEmpty)
        {
            _dragFrom = e.Location;
            _dragStartFocus = _focus;
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if(_dragFrom is Point start)
        {
            var fitted = Fitted;
            var source = Background.SourceSize;
            if(fitted.Width > 0)
            {
                // Move by as much of the picture as the pointer crossed in the preview.
                var scale = (float)source.Width / fitted.Width;
                var dx = (e.X - start.X) * scale / source.Width;
                var dy = (e.Y - start.Y) * scale / source.Height;

                _focus = new PointF(Math.Clamp(_dragStartFocus.X + dx, 0, 1),
                                    Math.Clamp(_dragStartFocus.Y + dy, 0, 1));
                Invalidate();
                FocusChanged?.Invoke(_focus);
            }
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _dragFrom = null;
        base.OnMouseUp(e);
    }
}
