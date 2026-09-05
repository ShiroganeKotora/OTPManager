using Microsoft.Win32;

namespace OtpManager.Setup;

/// <summary>
/// Removes everything the installer created and everything the app wrote, then takes its own
/// directory with it.
/// <para>
/// A program cannot delete the directory it is running from, so the last step is done from a copy
/// of this executable in the temp folder, started with <c>--finish</c>.
/// </para>
/// </summary>
internal static class Uninstaller
{
    private static int Main(string[] args)
    {
        var finish = args.FirstOrDefault(a => a.StartsWith("--finish:", StringComparison.Ordinal));
        if(finish != null) return DeleteInstallDirectory(finish["--finish:".Length..]);

        var here = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var hasData = Directory.Exists(Setup.DataDirectory);

        var warning = hasData
            ? "登録されているワンタイムパスワードのシークレットもすべて削除されます。" + Environment.NewLine +
              "元に戻すことはできません。バックアップを取っていない場合、各サービスの" + Environment.NewLine +
              "復旧コードでしか復元できなくなります。" + Environment.NewLine + Environment.NewLine
            : "";

        if(!Setup.Ask(
            $"{Setup.DisplayName} をアンインストールします。" + Environment.NewLine + Environment.NewLine +
            warning +
            "削除するもの:" + Environment.NewLine +
            $"  {here}" + Environment.NewLine +
            $"  {Setup.DataDirectory}" + Environment.NewLine +
            "  デスクトップとスタートメニューのショートカット" + Environment.NewLine +
            "  PATH の登録、スタートアップの登録、「アプリと機能」の登録" + Environment.NewLine + Environment.NewLine +
            "続行しますか？"))
        {
            return 1;
        }

        // Nothing here can be removed while the app still holds its own files.
        if(!Setup.StopRunningApp(here, ask: false)) return 1;

        var failed = new List<string>();
        Try(() => Delete(Setup.DesktopShortcut), "デスクトップのショートカット", failed);
        Try(() => Delete(Setup.StartMenuShortcut), "スタートメニューのショートカット", failed);
        Try(() => Setup.RemoveFromPath(here), "PATH の登録", failed);
        Try(RemoveAutostart, "スタートアップの登録", failed);
        Try(RemoveUninstallEntry, "「アプリと機能」の登録", failed);
        Try(() => { if(Directory.Exists(Setup.DataDirectory)) Directory.Delete(Setup.DataDirectory, true); },
            "設定とアカウント", failed);

        if(failed.Count > 0)
        {
            Setup.Tell("次の項目を削除できませんでした。" + Environment.NewLine + Environment.NewLine +
                       string.Join(Environment.NewLine, failed.Select(f => "  " + f)) + Environment.NewLine + Environment.NewLine +
                       "残りの削除は続行します。", Setup.MbIconWarning);
        }

        HandOffFinalDeletion(here);
        return 0;
    }

    private static void Try(Action action, string label, List<string> failed)
    {
        try { action(); }
        catch(Exception ex) { failed.Add($"{label}（{ex.Message}）"); }
    }

    private static void Delete(string path)
    {
        if(File.Exists(path)) File.Delete(path);
    }

    private static void RemoveAutostart()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Setup.AutostartKey, writable: true);
        key?.DeleteValue(Setup.AutostartValue, throwOnMissingValue: false);
    }

    private static void RemoveUninstallEntry()
    {
        using var parent = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall", writable: true);
        parent?.DeleteSubKeyTree(Setup.Product, throwOnMissingSubKey: false);
    }

    /// <summary>Copies itself out of the way and lets the copy remove the install directory.</summary>
    private static void HandOffFinalDeletion(string installDirectory)
    {
        try
        {
            var copy = Path.Combine(Path.GetTempPath(), $"{Setup.Product}-uninstall-{Guid.NewGuid():N}.exe");
            File.Copy(Environment.ProcessPath!, copy);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = copy,
                Arguments = $"--finish:\"{installDirectory}\"",
                UseShellExecute = true,
            });
        }
        catch(Exception ex)
        {
            Setup.Tell($"インストール先のフォルダを削除できませんでした。手動で削除してください。" +
                       $"{Environment.NewLine}{Environment.NewLine}{installDirectory}" +
                       $"{Environment.NewLine}{Environment.NewLine}{ex.Message}", Setup.MbIconWarning);
        }
    }

    private static int DeleteInstallDirectory(string directory)
    {
        directory = directory.Trim('"');

        // The program that started this one is still shutting down and still holds its own file.
        for(var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                if(Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
                Setup.Tell($"{Setup.DisplayName} をアンインストールしました。");
                return 0;
            }
            catch(Exception)
            {
                Thread.Sleep(250);
            }
        }

        Setup.Tell("インストール先のフォルダを削除できませんでした。手動で削除してください。" +
                   Environment.NewLine + Environment.NewLine + directory, Setup.MbIconWarning);
        return 2;
    }
}
