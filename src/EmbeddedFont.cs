using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace OtpManager;

/// <summary>
/// One icon font shipped inside the executable. Glyphs drawn from here must go through GDI+
/// (<c>Graphics.DrawString</c>): GDI, and therefore <c>TextRenderer</c> and the stock controls,
/// cannot see a font that was never installed on the machine.
/// </summary>
internal sealed class EmbeddedFont
{
    private readonly PrivateFontCollection _collection = new();
    private readonly Dictionary<float, Font> _cache = [];
    private readonly FontFamily? _family;

    public EmbeddedFont(string resourceName) => _family = Load(resourceName);

    /// <summary>True when the font loaded; callers fall back to plain text when it did not.</summary>
    public bool Available => _family != null;

    public Font Get(float size)
    {
        if(_cache.TryGetValue(size, out var cached)) return cached;

        var font = _family != null ? new Font(_family, size, GraphicsUnit.Pixel)
                                   : new Font(SystemFonts.DefaultFont.FontFamily, size, GraphicsUnit.Pixel);
        _cache[size] = font;
        return font;
    }

    private FontFamily? Load(string resourceName)
    {
        try
        {
            using var stream = typeof(EmbeddedFont).Assembly.GetManifestResourceStream(resourceName);
            if(stream == null) return null;

            var bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);

            // AddMemoryFont copies from unmanaged memory, so the block has to outlive the call.
            var handle = Marshal.AllocCoTaskMem(bytes.Length);
            Marshal.Copy(bytes, 0, handle, bytes.Length);
            _collection.AddMemoryFont(handle, bytes.Length);

            return _collection.Families.Length > 0 ? _collection.Families[0] : null;
        }
        catch(Exception)
        {
            // An icon is not worth failing startup over.
            return null;
        }
    }
}
