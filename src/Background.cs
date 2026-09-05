using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace OtpManager;

/// <summary>
/// The optional picture behind the list.
/// <para>
/// The image is fitted the way a wallpaper is: scaled until it covers the whole area, with whatever
/// does not fit cropped away around a point the user picks. It is then washed with the theme's own
/// background colour so the cards stay readable - the picture is meant to be sensed, not read.
/// </para>
/// <para>
/// The result is rendered once per size into a bitmap the size of the visible list, and every row
/// blits its own slice out of it. That is what keeps the picture continuous across controls that
/// know nothing about each other.
/// </para>
/// </summary>
internal static class Background
{
    private static Bitmap? _source;
    private static Bitmap? _cache;
    private static Size _cacheSize;
    private static bool _cacheDark;
    private static double _cacheOpacity;
    private static PointF _cacheFocus;

    /// <summary>The rendered backdrop for the current size, or null when there is no picture.</summary>
    public static Bitmap? Cache => _cache;

    public static bool HasImage => _source != null;

    public static string FilePath => Path.Combine(AccountStore.Directory, "background.png");

    /// <summary>Raised when the picture or its framing changes, so open windows can repaint.</summary>
    public static event Action? Changed;

    public static void Load()
    {
        Release();
        try
        {
            if(!Settings.Current.BackgroundEnabled || !File.Exists(FilePath)) return;

            // Read through a copy: opening the file directly would keep it locked for the session.
            using var stream = new MemoryStream(File.ReadAllBytes(FilePath));
            using var loaded = new Bitmap(stream);
            _source = new Bitmap(loaded);
        }
        catch(Exception)
        {
            _source = null;
        }
        Changed?.Invoke();
    }

    /// <summary>Stores a chosen picture beside the accounts, so moving the original cannot break it.</summary>
    public static void Adopt(string path)
    {
        using(var loaded = new Bitmap(path))
        using(var copy = new Bitmap(loaded))
        {
            Directory.CreateDirectory(AccountStore.Directory);
            copy.Save(FilePath, ImageFormat.Png);
        }
        Load();
    }

    public static void Forget()
    {
        Release();
        try { if(File.Exists(FilePath)) File.Delete(FilePath); }
        catch(Exception) { }
        Changed?.Invoke();
    }

    private static void Release()
    {
        _source?.Dispose();
        _source = null;
        _cache?.Dispose();
        _cache = null;
        _cacheSize = Size.Empty;
    }

    public static Size SourceSize => _source?.Size ?? Size.Empty;

    /// <summary>Draws the source into a rectangle the way the list will, for the settings preview.</summary>
    public static void DrawSource(Graphics g, Rectangle target)
    {
        if(_source == null) return;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(_source, target);
    }

    /// <summary>
    /// Rebuilds the backdrop when the area, theme or framing has changed. Cheap to call every paint.
    /// </summary>
    public static void Prepare(Size area)
    {
        if(_source == null || area.Width <= 0 || area.Height <= 0) return;

        var focus = new PointF((float)Settings.Current.BackgroundFocusX, (float)Settings.Current.BackgroundFocusY);
        var opacity = Settings.Current.BackgroundOpacity;

        if(_cache != null && _cacheSize == area && _cacheDark == Theme.IsDark
           && Math.Abs(_cacheOpacity - opacity) < 0.001 && _cacheFocus == focus) return;

        _cache?.Dispose();
        _cache = Render(area, focus, opacity);
        _cacheSize = area;
        _cacheDark = Theme.IsDark;
        _cacheOpacity = opacity;
        _cacheFocus = focus;
    }

    private static Bitmap Render(Size area, PointF focus, double opacity)
    {
        var rendered = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(rendered);
        g.Clear(ListStyle.ListBackground);

        if(_source != null)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(_source, CoverBounds(_source.Size, area, focus));
        }

        // The wash is what turns a picture into a backdrop: the higher the opacity setting, the
        // less of the theme colour is laid over it.
        var wash = (int)Math.Round(255 * (1 - Math.Clamp(opacity, 0, 1)));
        if(wash > 0)
        {
            using var brush = new SolidBrush(Color.FromArgb(wash, ListStyle.ListBackground));
            g.FillRectangle(brush, 0, 0, area.Width, area.Height);
        }
        return rendered;
    }

    /// <summary>
    /// Where the picture lands so that it covers the area completely: scaled by whichever axis needs
    /// the most, then slid so the chosen point sits where the user put it.
    /// </summary>
    public static Rectangle CoverBounds(Size source, Size area, PointF focus)
    {
        var scale = Math.Max((float)area.Width / source.Width, (float)area.Height / source.Height);
        var width = (int)Math.Ceiling(source.Width * scale);
        var height = (int)Math.Ceiling(source.Height * scale);

        // The focus is a point in the picture; put it in the middle of the area, then keep the
        // picture's edges from sliding inside it.
        var x = (int)Math.Round(area.Width / 2f - width * focus.X);
        var y = (int)Math.Round(area.Height / 2f - height * focus.Y);
        x = Math.Clamp(x, Math.Min(0, area.Width - width), 0);
        y = Math.Clamp(y, Math.Min(0, area.Height - height), 0);

        return new Rectangle(x, y, width, height);
    }

    /// <summary>Paints the slice of the backdrop that sits behind one child of the list.</summary>
    public static void PaintBehind(Graphics g, Control control, Color flat)
    {
        g.Clear(flat);
        if(_cache == null) return;
        g.DrawImage(_cache, -control.Left, -control.Top);
    }

    public static void Announce() => Changed?.Invoke();
}
