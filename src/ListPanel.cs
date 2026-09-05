namespace OtpManager;

/// <summary>
/// The scrolling list. A plain panel cannot take focus, so clicking an empty part of it makes
/// Windows Forms hand focus to the first control in the tab order instead - which puts the caret in
/// the filter box for no reason. Being selectable keeps the click where the user aimed it.
/// </summary>
internal sealed class ListPanel : Panel
{
    public ListPanel()
    {
        SetStyle(ControlStyles.Selectable, true);

        // Focusable by mouse, but skipped when tabbing: there is nothing here to type into.
        TabStop = false;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        Background.Prepare(ClientSize);
        base.OnPaintBackground(e);

        var cache = Background.Cache;
        if(cache == null) return;

        // Scrolling moves this control's own drawing origin along with the content, so the backdrop
        // is pushed back by the same amount to keep it still against the window.
        e.Graphics.DrawImage(cache, 0, -AutoScrollPosition.Y);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if(!Focused) Focus();
        base.OnMouseDown(e);
    }
}
