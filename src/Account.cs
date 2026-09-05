using System.Text;
using System.Text.Json.Serialization;

namespace OtpManager;

internal sealed class Account
{
    /// <summary>Stable identity, so the layout can refer to this account without depending on order.</summary>
    public string Id { get; set; } = "";

    public string Issuer { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>Name of the group this account sits under. Empty means ungrouped.</summary>
    public string Group { get; set; } = "";

    /// <summary>Base32 secret, encrypted at rest by <see cref="AccountStore"/>. Never written in the clear.</summary>
    public string Secret { get; set; } = "";

    public int Digits { get; set; } = 6;
    public int Period { get; set; } = 30;
    public string Algorithm { get; set; } = "SHA1";

    [JsonIgnore]
    public string Title => Issuer.Length > 0 && Name.Length > 0 ? $"{Issuer} — {Name}"
                         : Issuer.Length > 0 ? Issuer
                         : Name.Length > 0 ? Name
                         : "(名称未設定)";

    public Account Clone() => (Account)MemberwiseClone();

    public string Code(long unixSeconds) => Totp.Generate(Base32.Decode(Secret), unixSeconds, Period, Digits, Algorithm);

    /// <summary>Parses the otpauth:// URI that QR codes encode, so a scanned secret can be pasted whole.</summary>
    public static bool TryParseUri(string text, out Account account, out string error)
    {
        account = new Account();
        error = "";
        try
        {
            var uri = new Uri(text.Trim());
            if(!uri.Scheme.Equals("otpauth", StringComparison.OrdinalIgnoreCase))
            {
                error = "otpauth:// で始まるURIではありません。";
                return false;
            }
            if(!uri.Host.Equals("totp", StringComparison.OrdinalIgnoreCase))
            {
                error = $"{uri.Host} 形式には対応していません（totp のみ対応）。";
                return false;
            }

            var query = ParseQuery(uri.Query);
            if(!query.TryGetValue("secret", out var secret) || string.IsNullOrWhiteSpace(secret))
            {
                error = "URIに secret が含まれていません。";
                return false;
            }
            account.Secret = secret.Trim();

            // The label is "Issuer:Account" or just "Account"; the issuer query parameter wins when both exist.
            var label = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
            var colon = label.IndexOf(':');
            if(colon >= 0)
            {
                account.Issuer = label[..colon].Trim();
                account.Name = label[(colon + 1)..].Trim();
            }
            else
            {
                account.Name = label.Trim();
            }
            if(query.TryGetValue("issuer", out var issuer) && issuer.Trim().Length > 0) account.Issuer = issuer.Trim();

            if(query.TryGetValue("digits", out var d) && int.TryParse(d, out var digits) && digits is >= 6 and <= 10) account.Digits = digits;
            if(query.TryGetValue("period", out var p) && int.TryParse(p, out var period) && period is >= 10 and <= 300) account.Period = period;
            if(query.TryGetValue("algorithm", out var a) && Totp.Algorithms.Contains(a.ToUpperInvariant())) account.Algorithm = a.ToUpperInvariant();

            if(!Base32.IsValid(account.Secret))
            {
                error = "URIの secret がBase32として解釈できません。";
                return false;
            }
            return true;
        }
        catch(UriFormatException)
        {
            error = "URIとして解釈できません。";
            return false;
        }
    }

    /// <summary>Minimal query-string parser, so the app needs nothing outside the base class library.</summary>
    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach(var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=');
            if(equals < 0) continue;
            result[Uri.UnescapeDataString(pair[..equals])] = Uri.UnescapeDataString(pair[(equals + 1)..]);
        }
        return result;
    }

    public string ToUri()
    {
        var label = Issuer.Length > 0 ? $"{Uri.EscapeDataString(Issuer)}:{Uri.EscapeDataString(Name)}" : Uri.EscapeDataString(Name);
        var sb = new StringBuilder($"otpauth://totp/{label}?secret={Secret}");
        if(Issuer.Length > 0) sb.Append($"&issuer={Uri.EscapeDataString(Issuer)}");
        sb.Append($"&algorithm={Algorithm}&digits={Digits}&period={Period}");
        return sb.ToString();
    }
}
