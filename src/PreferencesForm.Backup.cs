using System.Security.Cryptography;

namespace OtpManager;

internal sealed partial class PreferencesForm
{
    private void Export()
    {
        if(_store.Accounts.Count == 0) { Warn("エクスポートするアカウントがありません。"); return; }

        using var save = new SaveFileDialog
        {
            Title = "バックアップの保存先",
            Filter = "OTP Manager バックアップ|*.otpbak|すべてのファイル|*.*",
            FileName = $"otpmanager-{DateTime.Now:yyyyMMdd}.otpbak",
        };
        if(save.ShowDialog(this) != DialogResult.OK) return;

        var passphrase = AskPassphrase("エクスポート", "バックアップを暗号化するパスフレーズ");
        if(passphrase == null) return;

        using(var confirm = new TextPromptForm("エクスポート", "確認のためもう一度入力してください", "", masked: true))
        {
            if(confirm.ShowDialog(this) != DialogResult.OK) return;
            if(confirm.Value != passphrase) { Warn("パスフレーズが一致しません。"); return; }
        }

        try
        {
            Backup.Export(save.FileName, passphrase, new Backup.Payload
            {
                Accounts = _store.Accounts,
                Groups = _store.Groups,
                Layout = _store.Order,
            });
            Inform($"{_store.Accounts.Count} 件をエクスポートしました。");
        }
        catch(Exception ex)
        {
            Warn($"エクスポートに失敗しました。{Environment.NewLine}{Environment.NewLine}{ex.Message}");
        }
    }

    private void Import()
    {
        using var open = new OpenFileDialog
        {
            Title = "バックアップを選択",
            Filter = "OTP Manager バックアップ|*.otpbak|すべてのファイル|*.*",
        };
        if(open.ShowDialog(this) != DialogResult.OK) return;

        var passphrase = AskPassphrase("インポート", "バックアップのパスフレーズ");
        if(passphrase == null) return;

        Backup.Payload payload;
        try
        {
            payload = Backup.Import(open.FileName, passphrase);
        }
        catch(CryptographicException ex)
        {
            Warn(ex.Message);
            return;
        }
        catch(Exception ex)
        {
            Warn($"読み込めませんでした。{Environment.NewLine}{Environment.NewLine}{ex.Message}");
            return;
        }

        // Restoring replaces everything; merging two lists is a different feature with its own
        // questions about duplicates, and guessing here would be the wrong kind of helpful.
        var answer = MessageBox.Show(this,
            $"{payload.Accounts.Count} 件を読み込みました。" +
            $"{Environment.NewLine}{Environment.NewLine}" +
            $"現在の {_store.Accounts.Count} 件はすべて置き換えられます。よろしいですか？",
            "OTP Manager", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if(answer != DialogResult.Yes) return;

        _store.Accounts.Clear();
        _store.Accounts.AddRange(payload.Accounts);
        _store.Groups.Clear();
        _store.Groups.AddRange(payload.Groups);
        _store.Order = payload.Layout;

        _dataChanged();
        Inform("インポートしました。");
    }

    private string? AskPassphrase(string title, string label)
    {
        using var prompt = new TextPromptForm(title, label, "", masked: true);
        if(prompt.ShowDialog(this) != DialogResult.OK) return null;
        if(prompt.Value.Length >= 8) return prompt.Value;

        Warn("パスフレーズは8文字以上にしてください。");
        return null;
    }
}
