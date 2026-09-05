namespace OtpManager;

/// <summary>
/// Icons drawn for this app rather than taken from an icon set. Built from the SVGs in
/// <c>assets/</c> by <c>tools/build_origlyph.py</c>; the codepoints are private use, because
/// these characters mean nothing outside this program.
/// </summary>
internal static class OriGlyphs
{
    private static readonly EmbeddedFont Resource = new("OtpManager.Resources.OTP_OriGlyph.ttf");

    public static bool Available => Resource.Available;
    public static Font Get(float size) => Resource.Get(size);

    /// <summary>A QR code with a pencil where "qr_code_2_add" has its plus sign.</summary>
    public static readonly string QrCodeEdit = ((char)0xE900).ToString();
}
