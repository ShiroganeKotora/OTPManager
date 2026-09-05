using System.Net.Http;

namespace OtpManager;

/// <summary>
/// Measures how far this machine's clock is from real time by asking a web server what time it
/// thinks it is. Nothing here changes the system clock - the result is only used to shift the
/// moment codes are generated for.
/// </summary>
internal static class TimeSync
{
    /// <summary>
    /// Cloudflare's trace endpoint: a few hundred bytes of plain text, needs no credentials, and
    /// reports the server's own time in its <c>ts=</c> line. Nothing about this machine or the
    /// stored accounts is sent - it is a bare GET.
    /// </summary>
    public const string Endpoint = "https://www.cloudflare.com/cdn-cgi/trace";

    public sealed record Result(bool Ok, double OffsetSeconds, string Message);

    public static async Task<Result> MeasureAsync(CancellationToken cancellation = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);

            var sent = DateTimeOffset.UtcNow;
            using var response = await http.SendAsync(request, cancellation);
            var received = DateTimeOffset.UtcNow;

            var body = await response.Content.ReadAsStringAsync(cancellation);
            var server = ParseTimestamp(body) ?? response.Headers.Date;
            if(server == null) return new Result(false, 0, "サーバーが時刻を返しませんでした。");

            // Blame half the round trip on each direction, which is the best a single request can do.
            var here = sent + (received - sent) / 2;
            return new Result(true, (server.Value - here).TotalSeconds, "");
        }
        catch(Exception ex)
        {
            return new Result(false, 0, ex.Message);
        }
    }

    /// <summary>
    /// Pulls the <c>ts=</c> line out of the trace response - a Unix timestamp carrying a fractional
    /// part. The Date header is the fallback for when the body is not in the shape expected.
    /// </summary>
    private static DateTimeOffset? ParseTimestamp(string body)
    {
        foreach(var line in body.Split('\n'))
        {
            if(!line.StartsWith("ts=", StringComparison.Ordinal)) continue;
            return double.TryParse(line[3..].Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var seconds)
                ? DateTimeOffset.UnixEpoch.AddSeconds(seconds)
                : null;
        }
        return null;
    }
}
