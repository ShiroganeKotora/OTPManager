using System.Drawing.Text;

namespace OtpManager;

/// <summary>
/// Renders an icon glyph into a bitmap. Menu items take an <see cref="Image"/> rather than drawing
/// text themselves, so glyphs meant for a menu have to be turned into pictures first.
/// </summary>
internal static class GlyphImage
{
    public static Bitmap Render(string glyph, Font font, int size, Color color)
    {
        var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.Transparent);
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        using var brush = new SolidBrush(color);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(glyph, font, brush, new RectangleF(0, 0, size, size), format);
        return bitmap;
    }
}
