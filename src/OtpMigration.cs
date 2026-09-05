using System.Text;

namespace OtpManager;

/// <summary>
/// Decodes an <c>otpauth-migration://</c> URI, the format authenticator apps use to hand several
/// accounts over at once. The payload is a protobuf message; only the handful of fields that matter
/// are read here, and everything else is skipped by wire type, so unknown fields cannot break it.
/// </summary>
internal static class OtpMigration
{
    public sealed record Result(List<Account> Accounts, int SkippedHotp, int Batch, int BatchCount);

    public static bool IsMigrationUri(string text) =>
        text.TrimStart().StartsWith("otpauth-migration://", StringComparison.OrdinalIgnoreCase);

    public static Result Parse(string uri)
    {
        var query = uri[(uri.IndexOf('?') + 1)..];
        string? data = null;
        foreach(var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=');
            if(equals < 0) continue;
            if(pair[..equals].Equals("data", StringComparison.OrdinalIgnoreCase)) data = pair[(equals + 1)..];
        }
        if(data == null) throw new FormatException("URIに data が含まれていません。");

        // '+' survives percent-decoding as a space in some readers; put it back before base64.
        var base64 = Uri.UnescapeDataString(data).Replace(' ', '+');
        var payload = Convert.FromBase64String(base64);

        var accounts = new List<Account>();
        var skipped = 0;
        var batch = 1;
        var batchCount = 1;

        var reader = new Reader(payload);
        while(!reader.Done)
        {
            var (field, wire) = reader.ReadTag();
            switch(field)
            {
                case 1 when wire == 2:
                    var parameters = ReadParameters(new Reader(reader.ReadBytes()));
                    if(parameters == null) skipped++;
                    else accounts.Add(parameters);
                    break;
                case 3 when wire == 0: batchCount = (int)reader.ReadVarint(); break;
                case 4 when wire == 0: batch = (int)reader.ReadVarint() + 1; break;
                default: reader.Skip(wire); break;
            }
        }
        return new Result(accounts, skipped, batch, batchCount);
    }

    /// <summary>Returns null for entries this app cannot represent, which today means HOTP.</summary>
    private static Account? ReadParameters(Reader reader)
    {
        var account = new Account { Period = 30 };
        var isTotp = true;

        while(!reader.Done)
        {
            var (field, wire) = reader.ReadTag();
            switch(field)
            {
                case 1 when wire == 2: account.Secret = Base32.Encode(reader.ReadBytes()); break;
                case 2 when wire == 2: account.Name = Encoding.UTF8.GetString(reader.ReadBytes()); break;
                case 3 when wire == 2: account.Issuer = Encoding.UTF8.GetString(reader.ReadBytes()); break;
                case 4 when wire == 0:
                    account.Algorithm = reader.ReadVarint() switch { 2 => "SHA256", 3 => "SHA512", _ => "SHA1" };
                    break;
                case 5 when wire == 0:
                    account.Digits = reader.ReadVarint() == 2 ? 8 : 6;
                    break;
                case 6 when wire == 0:
                    isTotp = reader.ReadVarint() != 1;
                    break;
                default: reader.Skip(wire); break;
            }
        }

        // The label often carries "Issuer:Account" even when the issuer field is also set.
        if(account.Issuer.Length > 0 && account.Name.StartsWith(account.Issuer + ":", StringComparison.Ordinal))
            account.Name = account.Name[(account.Issuer.Length + 1)..].Trim();

        return isTotp && account.Secret.Length > 0 ? account : null;
    }

    private sealed class Reader(byte[] buffer)
    {
        private int _position;

        public bool Done => _position >= buffer.Length;

        public (int Field, int Wire) ReadTag()
        {
            var tag = ReadVarint();
            return ((int)(tag >> 3), (int)(tag & 0x07));
        }

        public ulong ReadVarint()
        {
            ulong value = 0;
            var shift = 0;
            while(true)
            {
                if(_position >= buffer.Length) throw new FormatException("データが途中で終わっています。");
                var b = buffer[_position++];
                value |= (ulong)(b & 0x7F) << shift;
                if((b & 0x80) == 0) return value;
                shift += 7;
                if(shift > 63) throw new FormatException("データが壊れています。");
            }
        }

        public byte[] ReadBytes()
        {
            var length = (int)ReadVarint();
            if(length < 0 || _position + length > buffer.Length) throw new FormatException("データが途中で終わっています。");
            var slice = buffer[_position..(_position + length)];
            _position += length;
            return slice;
        }

        public void Skip(int wire)
        {
            switch(wire)
            {
                case 0: ReadVarint(); break;
                case 1: _position += 8; break;
                case 2: ReadBytes(); break;
                case 5: _position += 4; break;
                default: throw new FormatException($"未知のデータ形式です（wire type {wire}）。");
            }
        }
    }
}
