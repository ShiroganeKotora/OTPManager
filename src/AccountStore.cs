using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OtpManager;

/// <summary>
/// Persists accounts and groups under %APPDATA%\OtpManager. Secrets are wrapped with DPAPI for the
/// current user, so the file is useless if copied to another machine or opened under another account.
/// </summary>
internal sealed class AccountStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("OtpManager.v1");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string Directory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OtpManager");

    public static string FilePath { get; } = Path.Combine(Directory, "accounts.json");

    public List<Account> Accounts { get; private set; } = [];
    public List<AccountGroup> Groups { get; private set; } = [];

    /// <summary>Display order of headings and accounts together. See <see cref="ListOrder"/>.</summary>
    public List<string> Order { get; set; } = [];

    private sealed class Document
    {
        public int Version { get; set; } = 3;
        public List<Account> Accounts { get; set; } = [];
        public List<AccountGroup> Groups { get; set; } = [];
        public List<string> Layout { get; set; } = [];
    }

    public void Load()
    {
        Accounts = [];
        Groups = [];
        if(!File.Exists(FilePath)) return;

        var text = File.ReadAllText(FilePath, Encoding.UTF8);

        // Version 1 was a bare array of accounts, before groups existed.
        var document = text.TrimStart().StartsWith('[')
            ? new Document { Accounts = JsonSerializer.Deserialize<List<Account>>(text) ?? [] }
            : JsonSerializer.Deserialize<Document>(text) ?? new Document();

        foreach(var account in document.Accounts)
        {
            try
            {
                account.Secret = Unprotect(account.Secret);
                Accounts.Add(account);
            }
            catch(CryptographicException)
            {
                // A secret that will not decrypt is worse than useless in the list - surface it instead of crashing.
                MessageBox.Show($"「{account.Title}」のシークレットを復号できませんでした。\n" +
                                "別のユーザーアカウントで保存された可能性があります。この項目は読み込まれません。",
                                "OTP Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        Groups = document.Groups.Where(g => g.Name.Length > 0).ToList();
        Order = document.Layout;
        Repair();
    }

    /// <summary>
    /// Brings the three lists back into agreement: unique group names, an id on every account, a
    /// group for every name an account claims, and a layout that mentions everything exactly once.
    /// </summary>
    public void Repair()
    {
        foreach(var account in Accounts)
            if(account.Id.Length == 0) account.Id = Guid.NewGuid().ToString("N");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        Groups = Groups.Where(g => seen.Add(g.Name)).ToList();

        foreach(var account in Accounts)
        {
            if(account.Group.Length == 0 || seen.Contains(account.Group)) continue;
            Groups.Add(new AccountGroup { Name = account.Group });
            seen.Add(account.Group);
        }

        Order = ListOrder.Repair(Order, Accounts, Groups);
    }

    public void Save()
    {
        System.IO.Directory.CreateDirectory(Directory);
        var document = new Document
        {
            Accounts = Accounts.Select(a => { var c = a.Clone(); c.Secret = Protect(a.Secret); return c; }).ToList(),
            Groups = Groups,
            Layout = Order,
        };

        // Write beside the target and swap, so a crash mid-write cannot leave a truncated file.
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(document, Json), Encoding.UTF8);
        File.Move(temp, FilePath, overwrite: true);
    }

    private static string Protect(string plain) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser));

    private static string Unprotect(string encoded) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(encoded), Entropy, DataProtectionScope.CurrentUser));
}
