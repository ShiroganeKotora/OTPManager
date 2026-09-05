using System.Drawing.Drawing2D;

namespace OtpManager;

/// <summary>
/// Owns the process lifetime. The window is a guest here: hiding it leaves the tray icon running,
/// and only "終了" actually ends the message loop.
/// </summary>
internal sealed class TrayContext : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly MainForm _form;
    private readonly Icon _generated;

    public TrayContext(bool startHidden)
    {
        _generated = BuildIcon();
        _form = new MainForm { Icon = _generated };

        var menu = new ContextMenuStrip();
        menu.Items.Add("表示(&O)", null, (_, _) => _form.ShowFromTray());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("終了(&X)", null, (_, _) => Quit());

        _icon = new NotifyIcon
        {
            Icon = _generated,
            Text = "OTP Manager",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => _form.ShowFromTray();
        _icon.MouseClick += (_, e) => { if(e.Button == MouseButtons.Left) _form.ShowFromTray(); };

        if(!startHidden) _form.Show();
    }

    private void Quit()
    {
        _form.PrepareForExit();
        _icon.Visible = false;
        ExitThread();
    }

    /// <summary>Drawn at run time so the project stays a plain set of .cs files with no binary assets.</summary>
    private static Icon BuildIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using(var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using(var brush = new SolidBrush(Color.FromArgb(0, 120, 215)))
                g.FillEllipse(brush, 1, 1, 30, 30);
            using(var pen = new Pen(Color.White, 3f))
            {
                g.DrawArc(pen, 8, 8, 16, 16, -60, 300);
                g.DrawLine(pen, 16, 16, 16, 8);
            }
        }
        return Icon.FromHandle(bitmap.GetHicon());
    }

    protected override void Dispose(bool disposing)
    {
        if(disposing)
        {
            _icon.Dispose();
            _form.Dispose();
            _generated.Dispose();
        }
        base.Dispose(disposing);
    }
}
