using System.Security.Cryptography;

namespace OtpManager;

/// <summary>RFC 6238 time-based one-time passwords.</summary>
internal static class Totp
{
    public static string Generate(byte[] secret, long unixSeconds, int period, int digits, string algorithm)
    {
        var counter = unixSeconds / period;
        var message = BitConverter.GetBytes(counter);
        if(BitConverter.IsLittleEndian) Array.Reverse(message);

        using var hmac = CreateHmac(algorithm, secret);
        var hash = hmac.ComputeHash(message);

        // Dynamic truncation: the low nibble of the last byte picks the 4-byte window.
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
                   | ((hash[offset + 1] & 0xFF) << 16)
                   | ((hash[offset + 2] & 0xFF) << 8)
                   | (hash[offset + 3] & 0xFF);

        var modulo = (int)Math.Pow(10, digits);
        return (binary % modulo).ToString().PadLeft(digits, '0');
    }

    /// <summary>
    /// How much of the current code's lifetime is left, from 1 down to 0. Taken from milliseconds
    /// rather than whole seconds so the progress bar can move continuously instead of in steps.
    /// </summary>
    public static float RemainingFraction(long unixMilliseconds, int period)
    {
        var window = period * 1000L;
        var elapsed = ((unixMilliseconds % window) + window) % window;
        return 1f - (float)elapsed / window;
    }

    private static HMAC CreateHmac(string algorithm, byte[] key) => algorithm.ToUpperInvariant() switch
    {
        "SHA1" => new HMACSHA1(key),
        "SHA256" => new HMACSHA256(key),
        "SHA512" => new HMACSHA512(key),
        _ => throw new NotSupportedException($"未対応のアルゴリズムです: {algorithm}"),
    };

    public static readonly string[] Algorithms = ["SHA1", "SHA256", "SHA512"];
}
