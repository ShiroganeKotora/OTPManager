using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OtpManager;

/// <summary>
/// Import and export of the whole account list.
/// <para>
/// The everyday store is sealed with DPAPI, which by design cannot be read on another machine - the
/// opposite of what a backup is for. So a backup carries the secrets encrypted under a passphrase
/// the user chooses instead: AES-GCM with a PBKDF2 key. Lose the passphrase and the file is scrap;
/// that is the point, and the dialog says so.
/// </para>
/// </summary>
internal static class Backup
{
    private const int Iterations = 210_000;
    private const int SaltBytes = 16;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private const int KeyBytes = 32;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public sealed class Payload
    {
        public List<Account> Accounts { get; set; } = [];
        public List<AccountGroup> Groups { get; set; } = [];
        public List<string> Layout { get; set; } = [];
    }

    private sealed class Envelope
    {
        public string App { get; set; } = "OtpManager";
        public int Version { get; set; } = 1;
        public string Kdf { get; set; } = "PBKDF2-SHA256";
        public int Iterations { get; set; } = Backup.Iterations;
        public string Salt { get; set; } = "";
        public string Nonce { get; set; } = "";
        public string Tag { get; set; } = "";
        public string Data { get; set; } = "";
    }

    public static void Export(string path, string passphrase, Payload payload)
    {
        var plain = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, Json));

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagBytes];

        using(var aes = new AesGcm(DeriveKey(passphrase, salt), TagBytes))
            aes.Encrypt(nonce, plain, cipher, tag);

        var envelope = new Envelope
        {
            Salt = Convert.ToBase64String(salt),
            Nonce = Convert.ToBase64String(nonce),
            Tag = Convert.ToBase64String(tag),
            Data = Convert.ToBase64String(cipher),
        };
        File.WriteAllText(path, JsonSerializer.Serialize(envelope, Json), Encoding.UTF8);
    }

    public static Payload Import(string path, string passphrase)
    {
        var envelope = JsonSerializer.Deserialize<Envelope>(File.ReadAllText(path, Encoding.UTF8))
                       ?? throw new FormatException("バックアップファイルとして読めません。");
        if(envelope.App != "OtpManager") throw new FormatException("このアプリのバックアップではありません。");
        if(envelope.Version != 1) throw new FormatException($"未対応のバックアップ形式です（version {envelope.Version}）。");

        var salt = Convert.FromBase64String(envelope.Salt);
        var nonce = Convert.FromBase64String(envelope.Nonce);
        var tag = Convert.FromBase64String(envelope.Tag);
        var cipher = Convert.FromBase64String(envelope.Data);
        var plain = new byte[cipher.Length];

        try
        {
            using var aes = new AesGcm(DeriveKey(passphrase, salt, envelope.Iterations), tag.Length);
            aes.Decrypt(nonce, cipher, tag, plain);
        }
        catch(CryptographicException)
        {
            // AES-GCM fails the same way for a wrong passphrase and for a damaged file.
            throw new CryptographicException("復号できませんでした。パスフレーズが違うか、ファイルが壊れています。");
        }

        return JsonSerializer.Deserialize<Payload>(Encoding.UTF8.GetString(plain))
               ?? throw new FormatException("バックアップの中身を読めません。");
    }

    private static byte[] DeriveKey(string passphrase, byte[] salt, int iterations = Iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passphrase), salt, iterations, HashAlgorithmName.SHA256, KeyBytes);
}
