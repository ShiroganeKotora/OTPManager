using System.Drawing.Text;

namespace OtpManager;

/// <summary>
/// A flat button that paints a single icon glyph. Owner drawn because the framework's own button
/// draws through GDI, which cannot see a font loaded from <c>PrivateFontCollection</c>.
/// </summary>
internal sealed class IconButton : Control
{
    private readonly string _glyph;
    private readonly Font _font;
    private bool _hover;
    private bool _pressed;

    public IconButton(string glyph, Font font)
    {
        _glyph = glyph;
        _font = font;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        ResizeRedraw = true;
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(_pressed ? ListStyle.IconButtonPressed
              : _hover ? ListStyle.IconButtonHover
              : Parent?.BackColor ?? BackColor);

        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using var brush = new SolidBrush(Enabled ? ListStyle.IconButtonText : ListStyle.Icon);
        g.DrawString(_glyph, _font, brush, ClientRectangle, format);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
}
