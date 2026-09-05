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
        Changed?.Invoke();
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
