using System.Drawing.Drawing2D;

namespace OtpManager;

/// <summary>
/// A short-lived message that floats over the list instead of taking a strip of the window for
/// itself. It sizes to its text, slides up as it appears, and goes away on its own.
/// </summary>
internal sealed class Toast : Control
{
    private const int PaddingX = 14;
    private const int PaddingY = 9;
    private const int Radius = 7;
    private const int SlideDistance = 10;
    private const int SlideMilliseconds = 120;

    private readonly Font _font;
    private DateTime _shownAt;
    private DateTime _until;

    public Toast()
    {
        _font = new Font(Font.FontFamily, 9f);
        Visible = false;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        ResizeRedraw = true;
        Cursor = Cursors.Hand;
    }

    /// <summary>How far the toast still has to travel, in pixels, as it slides into place.</summary>
    public int SlideOffset
    {
        get
        {
            var elapsed = (DateTime.UtcNow - _shownAt).TotalMilliseconds;
            if(elapsed >= SlideMilliseconds) return 0;
            return (int)(SlideDistance * (1 - elapsed / SlideMilliseconds));
        }
    }

    public bool Sliding => SlideOffset > 0;

    public Size Measure(string message) =>
        TextRenderer.MeasureText(message, _font) + new Size(PaddingX * 2, PaddingY * 2);

    public void Show(string message, TimeSpan duration)
    {
        Text = message;
        Size = Measure(message);
        _shownAt = DateTime.UtcNow;
        _until = _shownAt + duration;
        Visible = true;
        BringToFront();
        Invalidate();
    }

    /// <summary>Returns true while the toast still wants to be on screen.</summary>
    public bool Tick()
    {
        if(!Visible) return false;
        if(DateTime.UtcNow <= _until) return true;

        Visible = false;
        return false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? BackColor);

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using(var path = Rounded(bounds, Radius))
        using(var fill = new SolidBrush(ListStyle.ToastFill))
            g.FillPath(fill, path);

        TextRenderer.DrawText(g, Text, _font, bounds, ListStyle.ToastInk,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    private static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        Visible = false;
        base.OnMouseClick(e);
    }

    protected override void Dispose(bool disposing)
    {
        if(disposing) _font.Dispose();
        base.Dispose(disposing);
    }
}
