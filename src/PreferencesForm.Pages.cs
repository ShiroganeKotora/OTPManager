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

        var readout = Note("", 22);

        var opacity = new Slider
        {
            Dock = DockStyle.Top,
            Minimum = Settings.MinCardOpacity,
            Maximum = Settings.MaxCardOpacity,
            Value = Math.Clamp(Settings.Current.CardOpacity, Settings.MinCardOpacity, Settings.MaxCardOpacity),
        };

        void ShowOpacity() => readout.Text = $"{opacity.Value}%（100%で背景が透けません）";
        ShowOpacity();

        opacity.ValueChanged += (_, _) =>
        {
            Settings.Current.CardOpacity = opacity.Value;
            Settings.Current.Save();
            ShowOpacity();

            // The list repaints on this the same way it does for the picture itself.
            Background.Announce();
        };

        return Page(
            Heading("表示"),
            Note("配色を選びます。「システムに従う」はWindowsのアプリモード設定に合わせます。", 34),
            system,
            light,
            dark,
            Note("すでに開いているダイアログには次回開いたときから反映されます。", 24),
            Note("透過度", 22),
            Note("カードとグループの濃さです。下げると背景の画像がその分だけ透けて見えます。" + Environment.NewLine +
                 "背景画像を設定していないときは、透かす相手がいないため見た目は変わりません。", 40),
            opacity,
            readout);
    }

    private Panel BuildBackground()
    {
        // Everything below is taller than the dialog, and the dialog is a fixed size on purpose,
        // so this one page scrolls instead of the window growing.
        var picker = new BackgroundPicker { Dock = DockStyle.Top, Height = 150, Tag = Theme.ManagedTag };
        var strip = new BackgroundStrip { Dock = DockStyle.Top, Viewport = _listViewport, Tag = Theme.ManagedTag };

        var strength = new Slider { Dock = DockStyle.Top, Minimum = 0, Maximum = 100 };
        var readout = Note("", 22);
        var count = Note("", 22);

        var interval = new NumericUpDown
        {
            Dock = DockStyle.Top,
            Width = 90,
            Minimum = Settings.MinBackgroundInterval,
            Maximum = Settings.MaxBackgroundInterval,
            Increment = 10,
            Value = Math.Clamp(Settings.Current.BackgroundIntervalSeconds,
                Settings.MinBackgroundInterval, Settings.MaxBackgroundInterval),
        };

        RadioButton Cycle(string text, string mode) => new()
        {
            Text = text,
            Dock = DockStyle.Top,
            Height = 26,
            Checked = Settings.Current.BackgroundCycle == mode,
            Tag = mode,
        };

        var never = Cycle("切り替えない", "None");
        var ordered = Cycle("順番に切り替える", "Sequential");
        var random = Cycle("ランダムに切り替える", "Random");

        // Nothing has been chosen yet in a file written by an older build, so fall back to "off".
        if(!never.Checked && !ordered.Checked && !random.Checked) never.Checked = true;

        var refreshing = false;

        void Refresh()
        {
            refreshing = true;

            var image = Background.Current;
            picker.Configure(_listViewport, image == null ? new PointF(0.5f, 0.5f)
                                                          : new PointF((float)image.FocusX, (float)image.FocusY));

            strength.Enabled = image != null;
            strength.Value = (int)Math.Round(Math.Clamp(image?.Opacity ?? 0.18, 0, 1) * 100);
            readout.Text = image == null ? "" : $"{strength.Value}%";

            var total = Background.Images.Count;
            count.Text = total == 0 ? "登録した画像はありません"
                                    : $"登録した画像　{total}枚　（{Background.CurrentIndex + 1}枚目を選択中）";

            strip.ScrollTo(Background.CurrentIndex);
            strip.Invalidate();

            refreshing = false;
        }

        strip.Selected += index =>
        {
            Background.Select(index);
            Refresh();
        };

        picker.FocusChanged += focus =>
        {
            var image = Background.Current;
            if(image == null) return;

            image.FocusX = focus.X;
            image.FocusY = focus.Y;
            Settings.Current.Save();

            // The tile is a small copy of the same crop, so it is wrong the moment the frame moves.
            Background.InvalidateThumbnail(image);
            strip.Invalidate();
            Background.Announce();
        };

        strength.ValueChanged += (_, _) =>
        {
            if(refreshing) return;

            var image = Background.Current;
            if(image == null) return;

            image.Opacity = strength.Value / 100.0;
            Settings.Current.Save();
            readout.Text = $"{strength.Value}%";
            Background.Announce();
        };

        foreach(var option in new[] { never, ordered, random })
        {
            option.CheckedChanged += (sender, _) =>
            {
                var button = (RadioButton)sender!;
                if(!button.Checked) return;

                Settings.Current.BackgroundCycle = (string)button.Tag!;
                Settings.Current.Save();

                // Otherwise a picture that has been up for longer than the interval jumps at once.
                Background.RestartCycle();
            };
        }

        interval.ValueChanged += (_, _) =>
        {
            Settings.Current.BackgroundIntervalSeconds = (int)interval.Value;
            Settings.Current.Save();
            Background.RestartCycle();
        };

        var add = Action("画像を追加...", (_, _) =>
        {
            using var dialog = new OpenFileDialog
            {
                Title = "背景に使う画像を選択",
                Filter = "画像ファイル|*.png;*.jpg;*.jpeg;*.bmp;*.gif|すべてのファイル|*.*",
                Multiselect = true,
            };
            if(dialog.ShowDialog(this) != DialogResult.OK) return;

            foreach(var file in dialog.FileNames)
            {
                try
                {
                    Background.Add(file);
                }
                catch(Exception ex)
                {
                    Warn($"画像を読み込めませんでした。{Environment.NewLine}{file}{Environment.NewLine}{Environment.NewLine}{ex.Message}");
                }
            }
            Refresh();
        });

        var remove = Action("選択中の画像を削除", (_, _) =>
        {
            if(Background.Current == null) return;

            Background.RemoveCurrent();
            Refresh();
        });

        Refresh();

        return Page(
            Heading("背景"),
            Note("登録した画像をリストの背後にうっすら敷きます。画像はウィンドウを覆うまで拡大され、" + Environment.NewLine +
                 "はみ出した分は切り取られます。大きい画像は、縦横の比を保ったまま" + Environment.NewLine +
                 "長辺1920ピクセルまで縮小して保存します。", 58),
            Row(add, remove),
            count,
            Note("並びはウィンドウと同じ縦横比で、実際に見える範囲を切り出しています。", 22),
            strip,
            Note("選択中の画像　濃さ", 22),
            strength,
            readout,
            Note("枠をドラッグして、画像のどこを見せるかを決めます。枠の外は切り取られる部分です。", 24),
            picker,
            Note("切り替え", 22),
            never,
            ordered,
            random,
            Note("切り替える間隔（秒）", 22),
            interval,
            Note("", 12));
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
            Note("方式: AES-256-GCM、鍵は PBKDF2-SHA256（600,000回）で導出", 24));
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
