using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace OtpManager;

/// <summary>
/// The optional pictures behind the list.
/// <para>
/// A picture is fitted the way a wallpaper is: scaled until it covers the whole area, with whatever
/// does not fit cropped away around a point the user picks. It is then washed with the theme's own
/// background colour so the cards stay readable - the picture is meant to be sensed, not read.
/// </para>
/// <para>
/// The result is rendered once per size into a bitmap the size of the visible list, and every row
/// blits its own slice out of it. That is what keeps the picture continuous across controls that
/// know nothing about each other.
/// </para>
/// <para>
/// Several pictures can be registered. Only the one on screen is kept decoded; the rest exist as
/// files and as small thumbnails, because a library of full-size bitmaps would cost tens of
/// megabytes to show a strip of postage stamps.
/// </para>
/// </summary>
internal static class Background
{
    /// <summary>
    /// The longest edge a stored picture is allowed to keep - full HD. A photo straight off a
    /// camera can be tens of megapixels, and rescaling that on every resize costs a quarter of a
    /// second per frame. The picture is washed out behind the cards anyway, so detail beyond this
    /// is never seen. Anything larger is scaled down by its long edge, keeping its proportions.
    /// </summary>
    private const int MaxSourceEdge = 1920;

    private static Bitmap? _source;
    private static string _sourceFile = "";

    private static Bitmap? _cache;
    private static Size _cacheSize;
    private static bool _cacheDark;
    private static double _cacheOpacity;
    private static PointF _cacheFocus;

    private static readonly Dictionary<string, Bitmap> _thumbnails = [];
    private static DateTime _lastSwitch = DateTime.UtcNow;
    private static readonly Random _shuffle = new();

    /// <summary>The rendered backdrop for the current size, or null when there is no picture.</summary>
    public static Bitmap? Cache => _cache;

    public static bool HasImage => _source != null;

    public static string Directory => Path.Combine(AccountStore.Directory, "background");

    /// <summary>Raised when the picture or its framing changes, so open windows can repaint.</summary>
    public static event Action? Changed;

    public static List<BackgroundImage> Images => Settings.Current.Backgrounds;

    /// <summary>The picture on screen, or null when the library is empty.</summary>
    public static BackgroundImage? Current
    {
        get
        {
            var images = Images;
            if(images.Count == 0) return null;

            var index = Math.Clamp(Settings.Current.BackgroundIndex, 0, images.Count - 1);
            return images[index];
        }
    }

    public static int CurrentIndex => Images.Count == 0 ? -1 : Math.Clamp(Settings.Current.BackgroundIndex, 0, Images.Count - 1);

    // --- library ---------------------------------------------------------

    public static void Load()
    {
        Migrate();
        Reload();
    }

    /// <summary>
    /// Carries a picture chosen by an older build into the library, then removes the file it came
    /// from - keeping it would mean two copies of the same picture, and the library is the only
    /// thing that reads it now.
    /// </summary>
    private static void Migrate()
    {
        var legacy = Path.Combine(AccountStore.Directory, "background.png");
        if(!File.Exists(legacy)) return;

        if(Images.Count == 0 && Settings.Current.BackgroundEnabled)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                var name = NewFileName();
                File.Copy(legacy, Path.Combine(Directory, name), overwrite: true);

                Images.Add(new BackgroundImage
                {
                    File = name,
                    FocusX = Settings.Current.BackgroundFocusX,
                    FocusY = Settings.Current.BackgroundFocusY,
                    Opacity = Settings.Current.BackgroundOpacity,
                });
                Settings.Current.BackgroundIndex = 0;
                Settings.Current.Save();
            }
            catch(Exception)
            {
                // The copy did not land, so the original is still the only version there is.
                return;
            }
        }

        try { File.Delete(legacy); }
        catch(Exception) { }
    }

    private static string NewFileName() => $"{Guid.NewGuid():N}.png";

    /// <summary>Stores a chosen picture beside the accounts, so moving the original cannot break it.</summary>
    public static void Add(string path)
    {
        using(var loaded = new Bitmap(path))
        using(var copy = Fit(loaded) ?? new Bitmap(loaded))
        {
            System.IO.Directory.CreateDirectory(Directory);
            var name = NewFileName();
            copy.Save(Path.Combine(Directory, name), ImageFormat.Png);
            Images.Add(new BackgroundImage { File = name });
            Settings.Current.BackgroundIndex = Images.Count - 1;
        }

        Settings.Current.Save();
        Reload();
    }

    /// <summary>Drops the picture on screen. The library going empty is how the background is turned off.</summary>
    public static void RemoveCurrent()
    {
        var index = CurrentIndex;
        if(index < 0) return;

        var image = Images[index];
        Images.RemoveAt(index);
        Forget(image);

        Settings.Current.BackgroundIndex = Math.Clamp(index, 0, Math.Max(0, Images.Count - 1));
        Settings.Current.Save();
        Reload();
    }

    private static void Forget(BackgroundImage image)
    {
        if(_thumbnails.Remove(image.File, out var thumbnail)) thumbnail.Dispose();
        try
        {
            var path = Path.Combine(Directory, image.File);
            if(File.Exists(path)) File.Delete(path);
        }
        catch(Exception)
        {
        }
    }

    public static void Select(int index)
    {
        if(index < 0 || index >= Images.Count || index == CurrentIndex) return;

        Settings.Current.BackgroundIndex = index;
        Settings.Current.Save();
        _lastSwitch = DateTime.UtcNow;
        Reload();
    }

    /// <summary>Reads the picture that should be on screen, and throws away what was there before.</summary>
    private static void Reload()
    {
        var image = Current;
        if(image != null && image.File == _sourceFile && _source != null)
        {
            Announce();
            return;
        }

        _source?.Dispose();
        _source = null;
        _sourceFile = "";
        _cache?.Dispose();
        _cache = null;
        _cacheSize = Size.Empty;

        if(image != null)
        {
            try
            {
                // Read through a copy: opening the file directly would keep it locked for the session.
                var path = Path.Combine(Directory, image.File);
                using var stream = new MemoryStream(File.ReadAllBytes(path));
                using var loaded = new Bitmap(stream);

                var shrunk = Fit(loaded);
                _source = shrunk ?? new Bitmap(loaded);
                _sourceFile = image.File;

                // A picture stored by an older build can still be huge. Write the smaller one back,
                // so the cost is paid once instead of on every start.
                if(shrunk != null)
                {
                    try { _source.Save(path, ImageFormat.Png); }
                    catch(Exception) { }
                }
            }
            catch(Exception)
            {
                _source = null;
                _sourceFile = "";
            }
        }

        Announce();
    }

    // --- cycling ---------------------------------------------------------

    /// <summary>
    /// Moves to the next picture once the interval has passed. Driven by the window's own timer
    /// rather than one of its own, so nothing keeps running after the window is gone.
    /// </summary>
    public static void TickCycle()
    {
        if(Images.Count < 2) return;

        var mode = Settings.Current.BackgroundCycle;
        if(mode != "Sequential" && mode != "Random") return;

        var interval = Math.Clamp(Settings.Current.BackgroundIntervalSeconds,
            Settings.MinBackgroundInterval, Settings.MaxBackgroundInterval);
        if((DateTime.UtcNow - _lastSwitch).TotalSeconds < interval) return;

        var index = CurrentIndex;
        if(mode == "Random")
        {
            // Never the one already up, or a "switch" would sometimes change nothing.
            var next = _shuffle.Next(Images.Count - 1);
            index = next >= index ? next + 1 : next;
        }
        else
        {
            index = (index + 1) % Images.Count;
        }

        Settings.Current.BackgroundIndex = index;
        Settings.Current.Save();
        _lastSwitch = DateTime.UtcNow;
        Reload();
    }

    /// <summary>Starts the clock again, so a settings change does not switch pictures immediately.</summary>
    public static void RestartCycle() => _lastSwitch = DateTime.UtcNow;

    // --- rendering -------------------------------------------------------

    /// <summary>
    /// A copy scaled down to <see cref="MaxSourceEdge"/>, or null when the picture already fits.
    /// This is the one place quality matters, and it happens once per picture.
    /// </summary>
    private static Bitmap? Fit(Bitmap image)
    {
        var longest = Math.Max(image.Width, image.Height);
        if(longest <= MaxSourceEdge) return null;

        var scale = (float)MaxSourceEdge / longest;
        var width = Math.Max(1, (int)Math.Round(image.Width * scale));
        var height = Math.Max(1, (int)Math.Round(image.Height * scale));

        var fitted = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(fitted);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(image, new Rectangle(0, 0, width, height));
        return fitted;
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
    /// Returns true when it actually rebuilt, which tells the caller that anything already holding a
    /// slice of the old one is now showing the wrong picture.
    /// </summary>
    public static bool Prepare(Size area)
    {
        var image = Current;
        if(_source == null || image == null || area.Width <= 0 || area.Height <= 0) return false;

        var focus = new PointF((float)image.FocusX, (float)image.FocusY);
        var opacity = image.Opacity;

        if(_cache != null && _cacheSize == area && _cacheDark == Theme.IsDark
           && Math.Abs(_cacheOpacity - opacity) < 0.001 && _cacheFocus == focus) return false;

        _cache?.Dispose();
        _cache = Render(area, focus, opacity);
        _cacheSize = area;
        _cacheDark = Theme.IsDark;
        _cacheOpacity = opacity;
        _cacheFocus = focus;
        return true;
    }

    private static Bitmap Render(Size area, PointF focus, double opacity)
    {
        var rendered = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(rendered);
        g.Clear(ListStyle.ListBackground);

        if(_source != null)
        {
            // This runs on every resize step. Bilinear is several times cheaper than bicubic and
            // the difference cannot be seen once the wash below is applied.
            g.InterpolationMode = InterpolationMode.Bilinear;
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

    // --- thumbnails ------------------------------------------------------

    /// <summary>
    /// The picture as it would appear behind a list of this shape, small. Cropping the thumbnail
    /// the same way the window will is the point: a landscape tile would show parts of the picture
    /// that never reach the screen.
    /// </summary>
    public static Bitmap? Thumbnail(BackgroundImage image, Size size)
    {
        if(size.Width <= 0 || size.Height <= 0) return null;

        var key = $"{image.File}|{size.Width}x{size.Height}|{image.FocusX:0.###}|{image.FocusY:0.###}";
        if(_thumbnails.TryGetValue(key, out var cached)) return cached;

        try
        {
            var path = Path.Combine(Directory, image.File);
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            using var loaded = new Bitmap(stream);

            var thumbnail = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            using(var g = Graphics.FromImage(thumbnail))
            {
                g.Clear(ListStyle.ListBackground);
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.DrawImage(loaded, CoverBounds(loaded.Size, size, new PointF((float)image.FocusX, (float)image.FocusY)));
            }

            // One entry per picture is enough; a changed crop replaces the one it supersedes.
            foreach(var stale in _thumbnails.Keys.Where(k => k.StartsWith(image.File + "|", StringComparison.Ordinal)).ToList())
            {
                _thumbnails[stale].Dispose();
                _thumbnails.Remove(stale);
            }

            _thumbnails[key] = thumbnail;
            return thumbnail;
        }
        catch(Exception)
        {
            return null;
        }
    }

    /// <summary>Throws away the thumbnail of one picture, after its framing was changed.</summary>
    public static void InvalidateThumbnail(BackgroundImage image)
    {
        foreach(var stale in _thumbnails.Keys.Where(k => k.StartsWith(image.File + "|", StringComparison.Ordinal)).ToList())
        {
            _thumbnails[stale].Dispose();
            _thumbnails.Remove(stale);
        }
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
