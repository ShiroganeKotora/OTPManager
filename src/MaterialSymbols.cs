namespace OtpManager;

/// <summary>
/// Glyphs taken from Google's Material Symbols (Apache License 2.0). The full variable font is
/// over 10 MB, so it is instanced and subset to just the characters used here - see the README.
/// </summary>
internal static class MaterialSymbols
{
    private static readonly EmbeddedFont Resource = new("OtpManager.Resources.qr_glyphs.ttf");

    public static bool Available => Resource.Available;
    public static Font Get(float size) => Resource.Get(size);

    /// <summary>"qr_code_2_add", at U+F658 in the Material Symbols codepoint map.</summary>
    public static readonly string QrCodeAdd = ((char)0xF658).ToString();

    /// <summary>"qr_code", at U+EF6B.</summary>
    public static readonly string QrCode = ((char)0xEF6B).ToString();

    /// <summary>"toc", at U+E8DE.</summary>
    public static readonly string Toc = ((char)0xE8DE).ToString();

    /// <summary>"ad_group", at U+E65B.</summary>
    public static readonly string AdGroup = ((char)0xE65B).ToString();

    /// <summary>"quick_reference", at U+E46E.</summary>
    public static readonly string QuickReference = ((char)0xE46E).ToString();

    /// <summary>"settings_applications", at U+E8B9.</summary>
    public static readonly string SettingsApplications = ((char)0xE8B9).ToString();
}
