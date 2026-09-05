using Microsoft.Win32;

namespace OtpManager;

/// <summary>
/// The per-user "Run" registry entry that starts the app with Windows. Only ever writes an entry
/// for this executable, and only when the user ticks the box in preferences.
/// </summary>
internal static class Autostart
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "OTP Manager";

    private static string Command => $"\"{Environment.ProcessPath}\" --tray";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue(ValueName) is string;
        }
        catch(Exception)
        {
            return false;
        }
    }

    /// <summary>Returns null on success, or a message explaining why it could not be changed.</summary>
    public static string? Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
            if(key == null) return "レジストリキーを開けませんでした。";

            if(enabled) key.SetValue(ValueName, Command);
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
            return null;
        }
        catch(Exception ex)
        {
            return ex.Message;
        }
    }
}
