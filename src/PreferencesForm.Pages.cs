using System.Diagnostics;

namespace OtpManager;

internal sealed partial class PreferencesForm
{
    private Panel BuildGeneral()
    {
        var autostart = new CheckBox
        {
            Text = "Windows起動時に自動的に開始する（トレイに常駐）",
            Dock = DockStyle.Top,
            Height = 30,
            Checked = Autostart.IsEnabled(),
        };
        autostart.CheckedChanged += (_, _) =>
        {
            var error = Autostart.Set(autostart.Checked);
            if(error == null) return;
            Warn($"自動起動の設定を変更できませんでした。{Environment.NewLine}{Environment.NewLine}{error}");
            autostart.Checked = Autostart.IsEnabled();
        };

        var path = new TextBox
        {
            Text = AccountStore.FilePath,
            Dock = DockStyle.Top,
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
        };

        return Page(
            Heading("全般"),
            autostart,
            Note("シークレットは Windows DPAPI で暗号化して保存されます。" +
                 "他のPCや他のユーザーアカウントでは復号できません。", 34),
            Note("保存先", 20),
            path,
            Row(Action("フォルダを開く", (_, _) => OpenStoreDirectory())));
    }

    private Panel BuildDisplay()
    {
        RadioButton Option(string text, ThemeMode mode) => new()
        {
            Text = text,
            Dock = DockStyle.Top,
            Height = 28,
            Checked = Theme.Mode == mode,
            Tag = mode,
        };

        var system = Option("システムに従う", ThemeMode.System);
        var light = Option("ライト", ThemeMode.Light);
        var dark = Option("ダーク", ThemeMode.Dark);

        foreach(var option in new[] { system, light, dark })
        {
            option.CheckedChanged += (sender, _) =>
            {
                var button = (RadioButton)sender!;
                if(!button.Checked) return;

                Theme.Set((ThemeMode)button.Tag!);
                Settings.Current.Theme = Theme.Mode.ToString();
                Settings.Current.Save();

                // This window is already on screen, so it has to be repainted in place.
                Theme.Style(this);
                Refresh();
            };
        }

        return Page(
            Heading("表示"),
            Note("配色を選びます。「システムに従う」はWindowsのアプリモード設定に合わせます。", 34),
            system,
            light,
            dark,
            Note("すでに開いているダイアログには次回開いたときから反映されます。", 24));
    }

    private Panel BuildBackground()
    {
        var picker = new BackgroundPicker { Dock = DockStyle.Fill, Tag = Theme.ManagedTag };

        var strength = new TrackBar
        {
            Dock = DockStyle.Top,
            Minimum = 0,
            Maximum = 100,
            TickFrequency = 10,
            Height = 40,
            Value = (int)Math.Round(Math.Clamp(Settings.Current.BackgroundOpacity, 0, 1) * 100),
        };

        void Refresh()
        {
            picker.Configure(_listViewport,
                new PointF((float)Settings.Current.BackgroundFocusX, (float)Settings.Current.BackgroundFocusY));
        }

        picker.FocusChanged += focus =>
        {
            Settings.Current.BackgroundFocusX = focus.X;
            Settings.Current.BackgroundFocusY = focus.Y;
            Settings.Current.Save();
            Background.Announce();
        };

        strength.ValueChanged += (_, _) =>
        {
            Settings.Current.BackgroundOpacity = strength.Value / 100.0;
            Settings.Current.Save();
            Background.Announce();
        };

        var choose = Action("画像を選択...", (_, _) =>
        {
            using var dialog = new OpenFileDialog
            {
                Title = "背景に使う画像を選択",
                Filter = "画像ファイル|*.png;*.jpg;*.jpeg;*.bmp;*.gif|すべてのファイル|*.*",
            };
            if(dialog.ShowDialog(this) != DialogResult.OK) return;

            try
            {
                Settings.Current.BackgroundEnabled = true;
                Settings.Current.Save();
                Background.Adopt(dialog.FileName);
                Refresh();
            }
            catch(Exception ex)
            {
                Warn($"画像を読み込めませんでした。{Environment.NewLine}{Environment.NewLine}{ex.Message}");
            }
        });

        var clear = Action("背景を消す", (_, _) =>
        {
            Settings.Current.BackgroundEnabled = false;
            Settings.Current.Save();
            Background.Forget();
            Refresh();
        });

        Refresh();

        return Page(
            Heading("背景"),
            Note("選んだ画像をリストの背後にうっすら敷きます。画像はウィンドウを覆うまで拡大され、" + Environment.NewLine +
                 "はみ出した分は切り取られます。ウィンドウを縦に伸ばすと、それに合わせて拡大されます。", 40),
            Row(choose, clear),
            Note("濃さ", 20),
            strength,
            Note("枠をドラッグして、画像のどこを見せるかを決めます。枠の外は切り取られる部分です。", 24),
            picker);
    }

    private Panel BuildBackup()
    {
        return Page(
            Heading("バックアップ"),
            Note("通常の保存先は Windows DPAPI で暗号化されているため、別のPCでは読めません。" + Environment.NewLine +
                 "バックアップはパスフレーズで暗号化するので、他のPCへ持ち出せます。", 46),
            Note("パスフレーズを忘れると復元できません。パスワード管理ツール等に控えてください。", 24),
            Row(Action("エクスポート...", (_, _) => Export()),
                Action("インポート...", (_, _) => Import())),
            Note("方式: AES-256-GCM、鍵は PBKDF2-SHA256（210,000回）で導出", 24));
    }

    private Panel BuildClock()
    {
        var status = new Label { Dock = DockStyle.Top, Height = 26, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) };
        UpdateClockStatus(status);

        var measure = Action("ずれを確認", (_, _) => { });
        measure.Click += async (_, _) =>
        {
            measure.Enabled = false;
            status.Text = "確認中...";
            var result = await TimeSync.MeasureAsync();
            measure.Enabled = true;

            if(!result.Ok)
            {
                UpdateClockStatus(status);
                Warn($"時刻を取得できませんでした。{Environment.NewLine}{Environment.NewLine}{result.Message}");
                return;
            }

            var seconds = (int)Math.Round(result.OffsetSeconds);

            // A measurement that comes back clean is itself the way to clear an old correction,
            // so there is nothing for a separate "remove correction" button to do.
            if(Math.Abs(result.OffsetSeconds) < 1)
            {
                Settings.Current.TimeOffsetSeconds = 0;
                Settings.Current.TimeCheckedUtc = DateTimeOffset.UtcNow.ToString("O");
                Settings.Current.Save();
                UpdateClockStatus(status);
                Inform("時計は正確です。補正は必要ありません。");
                return;
            }

            var answer = MessageBox.Show(this,
                $"この端末の時計は実際の時刻より {FormatOffset(result.OffsetSeconds)}。" +
                $"{Environment.NewLine}{Environment.NewLine}" +
                $"コード生成時に {seconds:+0;-0} 秒の補正を適用しますか？（Windowsの時計は変更しません）",
                "OTP Manager", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if(answer != DialogResult.Yes) { UpdateClockStatus(status); return; }

            Settings.Current.TimeOffsetSeconds = seconds;
            Settings.Current.TimeCheckedUtc = DateTimeOffset.UtcNow.ToString("O");
            Settings.Current.Save();
            UpdateClockStatus(status);
        };

        return Page(
            Heading("時刻の同期"),
            Note("ワンタイムパスワードは協定世界時（UTC）を基準に計算されるため、" + Environment.NewLine +
                 "タイムゾーンの設定は結果に影響しません。東京（GMT+9:00）でも他の地域でも同じです。" + Environment.NewLine +
                 "問題になるのは時計そのもののずれだけで、30秒ずれるとコードが1つ分食い違います。", 62),
            status,
            Row(measure),
            Note("確認を押したときだけ次のURLへ接続し、返ってきた時刻と比較します。" + Environment.NewLine +
                 TimeSync.Endpoint + Environment.NewLine +
                 "送信するのはこの要求だけで、登録内容や端末の情報は一切送りません。" + Environment.NewLine +
                 "補正はこのアプリの中だけで使われ、Windowsの時計は変更しません。", 74));
    }

    private static string FormatOffset(double seconds) =>
        seconds >= 0 ? $"{seconds:0.0} 秒遅れています" : $"{-seconds:0.0} 秒進んでいます";

    private static void UpdateClockStatus(Label status)
    {
        var offset = Settings.Current.TimeOffsetSeconds;
        var checkedAt = DateTimeOffset.TryParse(Settings.Current.TimeCheckedUtc, out var when)
            ? $"（最終確認 {when.ToLocalTime():yyyy-MM-dd HH:mm}）"
            : "";
        status.Text = offset == 0 ? $"補正なし {checkedAt}" : $"補正 {offset:+0;-0} 秒 {checkedAt}";
    }

    private Panel BuildSource()
    {
        var licences = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
            Text = string.Join(Environment.NewLine, LicenceText),
        };

        return Page(
            Heading("ソースコード"),
            Note(RepositoryUrl, 24),
            Row(Action("リポジトリを開く", (_, _) => OpenRepository())),
            Note("ライセンス", 22),
            licences);
    }

    private const string RepositoryUrl = "https://github.com/ShiroganeKotora/OTPManager";

    private void OpenRepository()
    {
        try
        {
            Process.Start(new ProcessStartInfo(RepositoryUrl) { UseShellExecute = true });
        }
        catch(Exception ex)
        {
            Warn($"開けませんでした。{Environment.NewLine}{Environment.NewLine}{ex.Message}");
        }
    }

    private static readonly string[] LicenceText =
    [
        "OTP Manager",
        "  Copyright (C) 2026 Shirogane Kotora",
        "",
        "OTP_OriGlyph  (src/Resources/OTP_OriGlyph.ttf)",
        "  Copyright (C) 2026 OTP Manager",
        "  Editted Google Material Symbols",
        "  Copyright (C) Google LLC",
        "  Apache License 2.0",
        "",
        "Material Symbols  (src/Resources/qr_glyphs.ttf)",
        "  Copyright (C) Google LLC",
        "  Apache License 2.0",
        "  https://github.com/google/material-design-icons",
        "",
        "ZXing.Net",
        "  Copyright (C) ZXing.Net Authors",
        "  Apache License 2.0",
        "  https://github.com/micjahn/ZXing.Net",
        "",
        "Apache License 2.0 :",
        "  https://www.apache.org/licenses/LICENSE-2.0",
    ];

    private void OpenStoreDirectory()
    {
        try
        {
            Directory.CreateDirectory(AccountStore.Directory);
            Process.Start(new ProcessStartInfo(AccountStore.Directory) { UseShellExecute = true });
        }
        catch(Exception ex)
        {
            Warn($"開けませんでした。{Environment.NewLine}{Environment.NewLine}{ex.Message}");
        }
    }
}
