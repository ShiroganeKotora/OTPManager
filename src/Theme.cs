using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace OtpManager;

internal enum ThemeMode { System, Light, Dark }

/// <summary>Which palette the app is drawing with, and how the window frame follows it.</summary>
internal static class Theme
{
    private const int DwmUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr window, string? application, string? subApplication);

    // Windows exposes dark scrollbars only through unnamed exports. They have sat at these ordinals
    // since Windows 10 1809 and are what dark-mode desktop apps use; if a future build moves them
    // the call fails and the scrollbar simply stays as it was.
    [DllImport("uxtheme.dll", EntryPoint = "#133")]
    private static extern bool AllowDarkModeForWindow(IntPtr window, bool allow);

    [DllImport("uxtheme.dll", EntryPoint = "#135")]
    private static extern int SetPreferredAppMode(int mode);

    private const int AppModeAllowDark = 1;
    private const int AppModeForceLight = 3;

    /// <summary>
    /// Makes one control's scrollbars follow the theme. Everything the app draws itself already
    /// does; a scrollbar belongs to Windows, and left alone it stays white inside a dark window.
    /// </summary>
    public static void ApplyToScrollBars(Control control)
    {
        try
        {
            if(!control.IsHandleCreated) return;

            SetPreferredAppMode(IsDark ? AppModeAllowDark : AppModeForceLight);
            AllowDarkModeForWindow(control.Handle, IsDark);
            SetWindowTheme(control.Handle, IsDark ? "DarkMode_Explorer" : "Explorer", null);
        }
        catch(Exception)
        {
        }
    }

    public static ThemeMode Mode { get; private set; } = ThemeMode.System;

    public static bool IsDark => Mode switch
    {
        ThemeMode.Light => false,
        ThemeMode.Dark => true,
        _ => SystemPrefersDark(),
    };

    /// <summary>Raised after <see cref="Mode"/> changes, so open windows can repaint themselves.</summary>
    public static event Action? Changed;

    public static void Load(string stored) =>
        Mode = Enum.TryParse<ThemeMode>(stored, ignoreCase: true, out var mode) ? mode : ThemeMode.System;

    public static void Set(ThemeMode mode)
    {
        if(Mode == mode) return;
        Mode = mode;
        ApplyColorMode();
        Changed?.Invoke();
    }

    /// <summary>
    /// Tells Windows Forms which palette the system-drawn parts should use. This is what reaches
    /// scrollbars: they belong to the window frame, not to any control, so no amount of setting
    /// BackColor touches them. Windows only has to be told once per palette, and it applies to
    /// windows created afterwards - which is why dialogs pick it up when they are next opened.
    /// </summary>
    public static void ApplyColorMode()
    {
        try
        {
#pragma warning disable WFO5001
            // "Classic" is the light one - the palette Windows Forms has always drawn with.
            Application.SetColorMode(IsDark ? SystemColorMode.Dark : SystemColorMode.Classic);
#pragma warning restore WFO5001
        }
        catch(Exception)
        {
        }
    }

    /// <summary>Windows exposes its own light/dark choice as a per-user registry value.</summary>
    private static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch(Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Recolours a dialog. Buttons are left to the system, and any control marked with the tag
    /// <see cref="ManagedTag"/> is left to whoever set its colours.
    /// </summary>
    public static void Style(Form form)
    {
        form.BackColor = ListStyle.DialogBackground;
        form.ForeColor = ListStyle.DialogText;
        Walk(form);
        form.HandleCreated += (_, _) => ApplyToTitleBar(form);
        if(form.IsHandleCreated) ApplyToTitleBar(form);

    }

    /// <summary>
    /// The older way of reaching a scrollbar, for the controls that have one. Applying this to a
    /// control that paints itself changes how its background is drawn, so it is aimed rather than
    /// walked over everything.
    /// </summary>
    private static void WalkScrollBars(Control parent)
    {
        foreach(Control control in parent.Controls)
        {
            if(control is ScrollableControl or ListBox or TextBoxBase) ApplyToScrollBars(control);
            WalkScrollBars(control);
        }
    }

    /// <summary>Marks a control that paints itself and must not be recoloured.</summary>
    public const string ManagedTag = "themed";

    /// <summary>Marks a label that should take the muted text colour rather than the plain one.</summary>
    public const string SubtleTag = "subtle";

    /// <summary>Marks a control that should take the sidebar background.</summary>
    public const string SidebarTag = "sidebar";

    private static void Walk(Control parent)
    {
        foreach(Control control in parent.Controls)
        {
            if(control.Tag as string == ManagedTag) continue;

            switch(control)
            {
                case TextBox text:
                    text.BackColor = ListStyle.InputBackground;
                    text.ForeColor = ListStyle.DialogText;
                    break;
                case ListBox list:
                    list.BackColor = ListStyle.SidebarBackground;
                    list.ForeColor = ListStyle.DialogText;
                    break;
                case ComboBox combo:
                    combo.BackColor = ListStyle.InputBackground;
                    combo.ForeColor = ListStyle.DialogText;
                    break;
                case NumericUpDown number:
                    number.BackColor = ListStyle.InputBackground;
                    number.ForeColor = ListStyle.DialogText;
                    break;
                case Button button:
                    // A system-drawn button ignores the background but honours the foreground, so a
                    // light text colour would leave it unreadable. Dark mode draws them by hand.
                    if(IsDark)
                    {
                        button.FlatStyle = FlatStyle.Flat;
                        button.UseVisualStyleBackColor = false;
                        button.BackColor = ListStyle.CardFill;
                        button.ForeColor = ListStyle.DialogText;
                        button.FlatAppearance.BorderColor = ListStyle.CardBorder;
                        button.FlatAppearance.MouseOverBackColor = ListStyle.CardHover;
                    }
                    else
                    {
                        button.FlatStyle = FlatStyle.System;
                        button.UseVisualStyleBackColor = true;
                        button.BackColor = SystemColors.Control;
                        button.ForeColor = SystemColors.ControlText;
                    }
                    break;
                case Label or CheckBox or RadioButton:
                    control.ForeColor = control.Tag as string == SubtleTag ? ListStyle.SubtleText : ListStyle.DialogText;
                    control.BackColor = Color.Transparent;
                    break;
                default:
                    if(control.Tag as string == SidebarTag) control.BackColor = ListStyle.SidebarBackground;
                    else if(control.HasChildren) control.BackColor = Color.Transparent;
                    break;
            }
            Walk(control);
        }
    }

    /// <summary>Paints the title bar to match. Silently does nothing on Windows versions without it.</summary>
    public static void ApplyToTitleBar(Form form)
    {
        try
        {
            var dark = IsDark ? 1 : 0;
            DwmSetWindowAttribute(form.Handle, DwmUseImmersiveDarkMode, ref dark, sizeof(int));
        }
        catch(Exception)
        {
        }
    }
}
