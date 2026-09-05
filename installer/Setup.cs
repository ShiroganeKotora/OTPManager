using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace OtpManager.Setup;

/// <summary>
/// Names and locations shared by the installer and the uninstaller. Anything the installer creates
/// is listed here so the uninstaller cannot forget to remove it.
/// </summary>
internal static partial class Setup
{
    public const string Product = "OtpManager";
    public const string DisplayName = "OTP Manager";
    public const string ExeName = "OtpManager.exe";
    public const string UninstallExeName = "uninstall.exe";
    public const string ShortcutName = DisplayName + ".lnk";

    /// <summary>Per-user install: no administrator rights, and nothing touched outside the profile.</summary>
    public static string DefaultInstallDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", Product);

    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Product);

    public static string DesktopShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ShortcutName);

    public static string StartMenuShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), ShortcutName);

    public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + Product;
    public const string AutostartKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string AutostartValue = DisplayName;
    public const string EnvironmentKey = "Environment";

    public const string Version = "1.0.0";

    // --- running instances ------------------------------------------------

    /// <summary>
    /// Anything under the install directory has to let go of its files before they can be replaced
    /// or removed. Asks first, then closes politely, then insists.
    /// </summary>
    public static bool StopRunningApp(string installDirectory, bool ask)
    {
        var running = FindRunning(installDirectory);
        if(running.Count == 0) return true;

        if(ask && !Ask(
            $"{DisplayName} が起動しています。" + Environment.NewLine + Environment.NewLine +
            "続けるには終了する必要があります。終了してよろしいですか？"))
        {
            return false;
        }

        foreach(var process in running)
        {
            try
            {
                if(process.CloseMainWindow()) process.WaitForExit(3000);
                if(!process.HasExited) { process.Kill(); process.WaitForExit(5000); }
            }
            catch(Exception)
            {
                // Already gone, or not ours to end; the caller finds out when a file will not move.
            }
            finally
            {
                process.Dispose();
            }
        }

        // File handles are released a moment after the process itself goes.
        Thread.Sleep(500);
        return true;
    }

    private static List<System.Diagnostics.Process> FindRunning(string installDirectory)
    {
        var name = Path.GetFileNameWithoutExtension(ExeName);
        var found = new List<System.Diagnostics.Process>();

        foreach(var process in System.Diagnostics.Process.GetProcessesByName(name))
        {
            try
            {
                // Only the copy that lives here; another install of the same app is not ours to end.
                var path = process.MainModule?.FileName;
                if(path != null && path.StartsWith(installDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(process);
                    continue;
                }
            }
            catch(Exception)
            {
                // A process whose path cannot be read is not one we can be sure about.
            }
            process.Dispose();
        }
        return found;
    }

    /// <summary>Where a previous install put itself, or null when there is none.</summary>
    public static (string Directory, string Version)? FindExistingInstall()
    {
        using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
        if(key?.GetValue("InstallLocation") is not string directory || directory.Length == 0) return null;
        if(!Directory.Exists(directory)) return null;

        return (directory, key.GetValue("DisplayVersion") as string ?? "?");
    }

    // --- message boxes ---------------------------------------------------

    public const uint MbOk = 0x00000000;
    public const uint MbYesNo = 0x00000004;
    public const uint MbIconWarning = 0x00000030;
    public const uint MbIconInformation = 0x00000040;
    public const uint MbIconError = 0x00000010;
    public const uint MbDefaultSecond = 0x00000100;
    public const int IdYes = 6;

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int MessageBox(IntPtr owner, string text, string caption, uint type);

    public static bool Ask(string text, uint icon = MbIconWarning) =>
        MessageBox(IntPtr.Zero, text, DisplayName, MbYesNo | icon | MbDefaultSecond) == IdYes;

    public static void Tell(string text, uint icon = MbIconInformation) =>
        MessageBox(IntPtr.Zero, text, DisplayName, MbOk | icon);

    // --- user PATH -------------------------------------------------------

    private const int HwndBroadcast = 0xFFFF;
    private const int WmSettingChange = 0x001A;

    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr SendMessageTimeout(IntPtr window, int message, IntPtr wParam, string lParam,
        int flags, int timeout, out IntPtr result);

    /// <summary>Tells already-running programs that the environment changed; without it, PATH edits
    /// are only picked up by processes started after the next sign-in.</summary>
    public static void AnnounceEnvironmentChange() =>
        SendMessageTimeout(HwndBroadcast, WmSettingChange, IntPtr.Zero, "Environment", 0x0002, 1000, out _);

    public static void AddToPath(string directory)
    {
        using var key = Registry.CurrentUser.OpenSubKey(EnvironmentKey, writable: true);
        if(key == null) return;

        var current = key.GetValue("Path", "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";
        var parts = current.Split(';', StringSplitOptions.RemoveEmptyEntries);
        if(parts.Any(p => string.Equals(p.TrimEnd(Path.DirectorySeparatorChar), directory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))) return;

        var updated = current.Length == 0 ? directory : current.TrimEnd(';') + ";" + directory;
        key.SetValue("Path", updated, RegistryValueKind.ExpandString);
        AnnounceEnvironmentChange();
    }

    public static void RemoveFromPath(string directory)
    {
        using var key = Registry.CurrentUser.OpenSubKey(EnvironmentKey, writable: true);
        if(key == null) return;

        var current = key.GetValue("Path", "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";
        var parts = current.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !string.Equals(p.TrimEnd(Path.DirectorySeparatorChar), directory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            .ToArray();

        key.SetValue("Path", string.Join(';', parts), RegistryValueKind.ExpandString);
        AnnounceEnvironmentChange();
    }

    // --- shortcuts -------------------------------------------------------

    /// <summary>
    /// Shortcuts are made through WScript.Shell rather than the IShellLink COM interfaces, which
    /// keeps this ahead-of-time compiled program free of COM interop for the sake of two files.
    /// </summary>
    public static void CreateShortcut(string shortcutPath, string target, string workingDirectory, string description)
    {
        var script =
            $"$s = (New-Object -ComObject WScript.Shell).CreateShortcut('{shortcutPath}'); " +
            $"$s.TargetPath = '{target}'; " +
            $"$s.WorkingDirectory = '{workingDirectory}'; " +
            $"$s.Description = '{description}'; " +
            "$s.Save()";
        RunPowerShell(script);
    }

    private static void RunPowerShell(string script)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        process?.WaitForExit(20000);
    }
}
