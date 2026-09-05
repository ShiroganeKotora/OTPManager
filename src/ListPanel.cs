using System.Runtime.InteropServices;

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

        // The backdrop is one picture spanning the whole panel, so growing the window has to repaint
        // all of it. Without this, Windows only paints the strip that was just exposed and the
        // picture stops where the panel used to end.
        ResizeRedraw = true;

        // Focusable by mouse, but skipped when tabbing: there is nothing here to type into.
        TabStop = false;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Every row blits its own slice of the backdrop, so a rebuilt one leaves them showing the
        // picture as it was framed for the old size. Nothing else repaints them when only the
        // height changes - their own bounds did not move - so they have to be told.
        if(Background.Prepare(ClientSize))
            foreach(Control child in Controls) child.Invalidate();

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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowScrollBar(IntPtr window, int bar, [MarshalAs(UnmanagedType.Bool)] bool show);

    private const int SbHorz = 0;
    private const int WmNcCalcSize = 0x0083;

    protected override void WndProc(ref Message m)
    {
        // The list is never meant to scroll sideways: the rows are laid out to the panel's own
        // width, and the window cannot be narrowed past the point where that still fits. AutoScroll
        // decides otherwise for a moment whenever the vertical bar appears and the rows have not
        // caught up yet, and a horizontal bar flickers into view during a drag-resize.
        //
        // Rather than chase that transient, the bar is taken away at the source. WM_NCCALCSIZE is
        // the message sent whenever the non-client area - which is where scrollbars live - is
        // recalculated, so this runs exactly when the bar would otherwise be put back.
        if(m.Msg == WmNcCalcSize && IsHandleCreated) ShowScrollBar(Handle, SbHorz, false);
        base.WndProc(ref m);
    }
}
