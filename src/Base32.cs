namespace OtpManager;

/// <summary>RFC 4648 base32, which is how every authenticator app exchanges secrets.</summary>
internal static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static byte[] Decode(string input)
    {
        if(string.IsNullOrWhiteSpace(input)) throw new FormatException("シークレットが空です。");

        var bits = 0;
        var value = 0;
        var bytes = new List<byte>();

        foreach(var raw in input)
        {
            // Users paste secrets with spaces and padding; both are noise here.
            if(raw is ' ' or '-' or '=' or '\t' or '\r' or '\n') continue;
            var index = Alphabet.IndexOf(char.ToUpperInvariant(raw));
            if(index < 0) throw new FormatException($"シークレットに使えない文字が含まれています: '{raw}'");

            value = (value << 5) | index;
            bits += 5;
            if(bits < 8) continue;

            bits -= 8;
            bytes.Add((byte)((value >> bits) & 0xFF));
        }

        if(bytes.Count == 0) throw new FormatException("シークレットが短すぎます。");
        return [.. bytes];
    }

    /// <summary>Encodes raw secret bytes, which is how they arrive inside a migration payload.</summary>
    public static string Encode(byte[] data)
    {
        var builder = new System.Text.StringBuilder();
        var bits = 0;
        var value = 0;

        foreach(var b in data)
        {
            value = (value << 8) | b;
            bits += 8;
            while(bits >= 5)
            {
                bits -= 5;
                builder.Append(Alphabet[(value >> bits) & 31]);
            }
        }
        if(bits > 0) builder.Append(Alphabet[(value << (5 - bits)) & 31]);
        return builder.ToString();
    }

    public static bool IsValid(string input)
    {
        try { Decode(input); return true; }
        catch(FormatException) { return false; }
    }
}
