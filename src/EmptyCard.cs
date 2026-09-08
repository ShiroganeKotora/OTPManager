using System.ComponentModel;

namespace OtpManager;

/// <summary>
/// What fills the list before the first account is added. A plain label would be opaque over the
/// whole area and hide the background picture, so this paints the backdrop like every row does and
/// puts the message on a card of its own - the same card the accounts will appear on.
/// </summary>
internal sealed class EmptyCard : Control
{
    private static readonly string[] Lines =
    [
        "アカウントがありません。",
        "「追加」から登録してください。",
    ];

    private const int PadX = 26;
    private const int PadY = 20;
    private const int LineGap = 6;

    private bool _dimmed;

    public EmptyCard()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.UserPaint, true);
        ResizeRedraw = true;
    }

    /// <summary>Pushed back while the QR card is open, the same way the rows are.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Dimmed
    {
        get => _dimmed;
        set
        {
            if(_dimmed == value) return;
            _dimmed = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Background.PaintBehind(g, this, _dimmed ? AccountRow.DimBackground : ListStyle.ListBackground);

        var sizes = Lines.Select(line => TextRenderer.MeasureText(g, line, Font)).ToArray();
        var textWidth = sizes.Max(s => s.Width);
        var textHeight = sizes.Sum(s => s.Height) + LineGap * (Lines.Length - 1);

        var card = new Rectangle(
            (Width - textWidth) / 2 - PadX,
            (Height - textHeight) / 2 - PadY,
            textWidth + PadX * 2,
            textHeight + PadY * 2);

        // The same treatment the account cards get, so the two read as one family.
        var fill = ListStyle.OverBackground(_dimmed ? AccountRow.DimBackground : ListStyle.CardFill, 226);
        using(var brush = new SolidBrush(fill)) g.FillRectangle(brush, card);
        using(var pen = new Pen(_dimmed ? ListStyle.Blend(ListStyle.CardBorder, AccountRow.DimBackground, 0.72f)
                                        : ListStyle.CardBorder))
            g.DrawRectangle(pen, card.Left, card.Top, card.Width - 1, card.Height - 1);

        var ink = _dimmed ? ListStyle.Blend(ListStyle.SubtleText, AccountRow.DimBackground, 0.6f)
                          : ListStyle.SubtleText;

        var y = card.Top + PadY;
        for(var i = 0; i < Lines.Length; i++)
        {
            var line = new Rectangle(card.Left, y, card.Width, sizes[i].Height);
            TextRenderer.DrawText(g, Lines[i], Font, line, ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            y += sizes[i].Height + LineGap;
        }
    }
}
