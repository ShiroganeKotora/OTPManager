using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using ZXing.Windows.Compatibility;

namespace OtpManager;

/// <summary>Reading and drawing QR codes, which is how authenticator secrets are normally exchanged.</summary>
internal static class QrCode
{
    /// <summary>Returns the decoded text, or null when the image holds no readable QR code.</summary>
    public static string? Decode(Bitmap image)
    {
        var reader = new BarcodeReader
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                TryHarder = true,
                TryInverted = true,
                PossibleFormats = [BarcodeFormat.QR_CODE],
            },
        };
        return reader.Decode(image)?.Text;
    }

    /// <summary>Unwraps an image that arrived as text, e.g. "data:image/png;base64,iVBOR...".</summary>
    public static bool TryDecodeDataUri(string text, out Bitmap image)
    {
        image = null!;
        if(!text.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) return false;

        var marker = text.IndexOf(";base64,", StringComparison.OrdinalIgnoreCase);
        if(marker < 0) return false;

        try
        {
            var payload = new string(text[(marker + 8)..].Where(c => !char.IsWhiteSpace(c)).ToArray());
            using var stream = new MemoryStream(Convert.FromBase64String(payload));
            using var decoded = new Bitmap(stream);

            // GDI+ keeps reading from the stream, so take an independent copy before it closes.
            image = new Bitmap(decoded);
            return true;
        }
        catch(Exception)
        {
            return false;
        }
    }

    public static Bitmap Encode(string text, int size)
    {
        var writer = new BarcodeWriter
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new QrCodeEncodingOptions
            {
                Width = size,
                Height = size,
                Margin = 1,
                CharacterSet = "UTF-8",
            },
        };
        return writer.Write(text);
    }
}
