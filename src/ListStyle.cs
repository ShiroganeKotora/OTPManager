namespace OtpManager;

/// <summary>
/// Shared measurements and colours for the list. Rows and headings are separate controls stacked
/// with no gaps, so a group's outline is drawn in pieces: the heading paints the top and sides, each
/// member paints the sides, and the last member closes the bottom. Keeping the numbers here is what
/// makes those pieces line up.
/// <para>
/// Every colour is a property rather than a constant, because the palette changes with the theme.
/// </para>
/// </summary>
internal static class ListStyle
{
    /// <summary>Inset of a group's outline from the edge of the list.</summary>
    public const int GroupMargin = 6;

    /// <summary>Gap between a group's outline and the cards inside it.</summary>
    public const int GroupPadding = 6;

    /// <summary>Inset of a card that is not in any group.</summary>
    public const int CardMargin = 8;

    /// <summary>Vertical gap between one card and the next.</summary>
    public const int CardGap = 3;

    /// <summary>Gap above and below a group's outline, matching the gap between two cards.</summary>
    public const int SectionGap = CardGap;

    private static Color Pick(int light, int dark) =>
        Color.FromArgb(unchecked((int)0xFF000000) | (Theme.IsDark ? dark : light));

    public static Color ListBackground => Pick(0xF5F6F8, 0x1E1F22);
    public static Color CardFill => Pick(0xFFFFFF, 0x2B2D31);
    public static Color CardHover => Pick(0xF0F5FB, 0x35383E);
    public static Color CardGhost => Pick(0xFAFBFC, 0x26282C);
    public static Color CardBorder => Pick(0xDEE2E8, 0x3B3E45);

    public static Color GroupFill => Pick(0xEEF0F4, 0x26282C);
    public static Color GroupBorder => Pick(0xC8CED6, 0x4A4E56);
    public static Color HeaderBand => Pick(0xE2E5EB, 0x32353B);
    public static Color HeaderBandHover => Pick(0xD8DCE4, 0x3B3E45);
    public static Color HeaderText => Pick(0x3C424A, 0xC9CFD8);
    public static Color HeaderCount => Pick(0x787E88, 0x878D96);

    public static Color Highlight => Pick(0x0078D7, 0x4A9EFF);
    public static Color HighlightFill => Pick(0xE2EEFA, 0x1D3A5A);
    public static Color HighlightBand => Pick(0xCDE2F6, 0x24466B);

    public static Color CodeAccent => Pick(0x0078D7, 0x5AA9F8);
    public static Color CodeWarn => Pick(0xD65914, 0xE8944A);
    public static Color CodeBroken => Pick(0xB22222, 0xE06C6C);
    public static Color Title => Pick(0x606060, 0x9AA1AB);
    public static Color ProgressTrack => Pick(0xE4E6EA, 0x3B3E45);
    public static Color Icon => Pick(0x969CA4, 0x7E858F);
    public static Color IconActive => Pick(0x0064BE, 0x6FB4FF);
    public static Color IconHoverFill => Pick(0xE2EAF4, 0x3D444D);
    public static Color IconButtonText => Pick(0x3C4652, 0xC3CAD3);
    public static Color IconButtonHover => Pick(0xECF2F9, 0x33373D);
    public static Color IconButtonPressed => Pick(0xDEE7F2, 0x3C424A);

    public static Color DimBackground => Pick(0xF2F3F5, 0x232427);
    public static Color ToastFill => Pick(0x2D3138, 0x3D424A);
    public static Color ToastInk => Pick(0xF2F4F7, 0xF0F2F5);

    public static Color DialogBackground => Pick(0xFFFFFF, 0x232427);
    public static Color DialogText => Pick(0x1A1A1A, 0xE2E5EA);
    public static Color InputBackground => Pick(0xFFFFFF, 0x2B2D31);
    public static Color SidebarBackground => Pick(0xF5F6F8, 0x1E1F22);
    public static Color SubtleText => Pick(0x5A6068, 0x9AA1AB);
    public static Color Warning => Pick(0xB43C14, 0xE8836A);

    /// <summary>Where a card sits inside its row.</summary>
    public static Rectangle CardBounds(int width, int rowHeight, bool inGroup)
    {
        var inset = inGroup ? GroupMargin + GroupPadding : CardMargin;
        return new Rectangle(inset, CardGap, Math.Max(0, width - inset * 2), Math.Max(0, rowHeight - CardGap * 2));
    }

    public static Color Blend(Color color, Color towards, float amount) => Color.FromArgb(
        (int)(color.R + (towards.R - color.R) * amount),
        (int)(color.G + (towards.G - color.G) * amount),
        (int)(color.B + (towards.B - color.B) * amount));
}
