using System.IO.Compression;
using Microsoft.Win32;

namespace OtpManager.Setup;

/// <summary>
/// Unpacks the application into the user's profile and registers the things Windows needs to know
/// about: shortcuts, the PATH entry, and an entry under "Apps &amp; features".
/// </summary>
internal static class Installer
{
    private const string PayloadResource = "OtpManager.Setup.Payload.app.zip";

    private static int Main(string[] args)
    {
        var silent = args.Any(a => a.Equals("--silent", StringComparison.OrdinalIgnoreCase));
        var chosen = args.FirstOrDefault(a => !a.StartsWith('-'));
        var existing = Setup.FindExistingInstall();

        // An existing install decides where this one goes, so an update lands on top of it rather
        // than leaving a second copy behind at the default location.
        var target = chosen ?? existing?.Directory ?? Setup.DefaultInstallDirectory;

        if(!silent && !Setup.Ask(Describe(existing, target)))
            return 1;

        // Files cannot be replaced while they are in use, and the app may be sitting in the tray.
        if(!Setup.StopRunningApp(target, ask: !silent))
            return 1;

        try
        {
            Install(target);
        }
        catch(Exception ex)
        {
            if(!silent) Setup.Tell($"インストールに失敗しました。{Environment.NewLine}{Environment.NewLine}{ex.Message}", Setup.MbIconError);
            return 2;
        }

        if(silent) return 0;

        var launch = Setup.Ask(
            (existing == null ? "インストールが完了しました。" : "更新が完了しました。") + Environment.NewLine + Environment.NewLine +
            $"  {target}" + Environment.NewLine + Environment.NewLine +
            "PATH の変更は、これから起動するプログラムから有効になります。" + Environment.NewLine + Environment.NewLine +
            "今すぐ起動しますか？", Setup.MbIconInformation);

        if(launch)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = Path.Combine(target, Setup.ExeName),
                WorkingDirectory = target,
                UseShellExecute = true,
            });
        }
        return 0;
    }

    /// <summary>
    /// Installing, updating and repairing all come down to the same thing - lay the files down
    /// again - so the only difference here is saying which one the user is about to do.
    /// </summary>
    private static string Describe((string Directory, string Version)? existing, string target)
    {
        var heading = existing == null
            ? $"{Setup.DisplayName} をインストールします。"
            : existing.Value.Version == Setup.Version
                ? $"{Setup.DisplayName} {Setup.Version} は既にインストールされています。" + Environment.NewLine +
                  "同じバージョンで上書きし、修復します。"
                : $"{Setup.DisplayName} を {existing.Value.Version} から {Setup.Version} へ更新します。";

        var kept = existing == null
            ? ""
            : "登録済みのアカウントと設定はそのまま残ります。" + Environment.NewLine + Environment.NewLine;

        return heading + Environment.NewLine + Environment.NewLine +
            (existing == null ? "インストール先:" : "対象:") + Environment.NewLine +
            $"  {target}" + Environment.NewLine + Environment.NewLine +
            kept +
            "あわせて次を作成します:" + Environment.NewLine +
            "  デスクトップとスタートメニューのショートカット" + Environment.NewLine +
            "  ユーザー環境変数 PATH への、上記インストール先の追加" + Environment.NewLine +
            "  「アプリと機能」への登録" + Environment.NewLine + Environment.NewLine +
            "管理者権限は不要で、変更はこのユーザーの範囲に留まります。" + Environment.NewLine + Environment.NewLine +
            "続行しますか？";
    }

    private static void Install(string target)
    {
        // A previous install would leave files the new one does not overwrite; clearing first keeps
        // an upgrade from silently inheriting them.
        if(Directory.Exists(target)) Directory.Delete(target, recursive: true);
        Directory.CreateDirectory(target);

        using(var payload = typeof(Installer).Assembly.GetManifestResourceStream(PayloadResource)
                            ?? throw new InvalidOperationException("インストーラにアプリ本体が含まれていません。"))
        using(var archive = new ZipArchive(payload, ZipArchiveMode.Read))
        {
            archive.ExtractToDirectory(target, overwriteFiles: true);
        }

        var exe = Path.Combine(target, Setup.ExeName);
        Setup.CreateShortcut(Setup.DesktopShortcut, exe, target, Setup.DisplayName);
        Setup.CreateShortcut(Setup.StartMenuShortcut, exe, target, Setup.DisplayName);
        Setup.AddToPath(target);
        WriteUninstallEntry(target, exe);
    }

    /// <summary>Registers the app under "Apps &amp; features", so it can be removed the usual way.</summary>
    private static void WriteUninstallEntry(string target, string exe)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Setup.UninstallKey, writable: true);
        if(key == null) return;

        var size = new DirectoryInfo(target).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);

        key.SetValue("DisplayName", Setup.DisplayName);
        key.SetValue("DisplayVersion", Setup.Version);
        key.SetValue("Publisher", "Shirogane Kotora");
        key.SetValue("DisplayIcon", exe);
        key.SetValue("InstallLocation", target);
        key.SetValue("UninstallString", $"\"{Path.Combine(target, Setup.UninstallExeName)}\"");
        key.SetValue("EstimatedSize", (int)(size / 1024), RegistryValueKind.DWord);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }
}
