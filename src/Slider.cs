using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace OtpManager;

/// <summary>
/// A slider with a round knob. The stock TrackBar draws a squared-off pointer and honours neither
/// ForeColor nor BackColor, so it stays light inside a dark window; this one follows the theme and
/// puts the handle where the setting reads as a level rather than a notched scale.
/// </summary>
internal sealed class Slider : Control
{
    private const int KnobRadius = 8;
    private const int TrackThickness = 4;

    private int _minimum;
    private int _maximum = 100;
    private int _value;
    private bool _dragging;
    private bool _hover;

    public Slider()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.UserPaint | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
        ResizeRedraw = true;
        TabStop = true;
        Height = 30;
    }

    public event EventHandler? ValueChanged;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Minimum
    {
        get => _minimum;
        set { _minimum = value; Value = _value; Invalidate(); }
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Maximum
    {
        get => _maximum;
        set { _maximum = value; Value = _value; Invalidate(); }
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => _value;
        set
        {
            var clamped = Math.Clamp(value, _minimum, _maximum);
            if(clamped == _value) return;

            _value = clamped;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private Rectangle TrackBounds
    {
        get
        {
            var y = (Height - TrackThickness) / 2;
            return new Rectangle(KnobRadius, y, Math.Max(1, Width - KnobRadius * 2), TrackThickness);
        }
    }

    private int KnobCentre
    {
        get
        {
            var span = Math.Max(1, _maximum - _minimum);
            var track = TrackBounds;
            return track.Left + (int)Math.Round((double)(_value - _minimum) / span * track.Width);
        }
    }

    private void SetValueFrom(int x)
    {
        var track = TrackBounds;
        var ratio = (double)(x - track.Left) / Math.Max(1, track.Width);
        Value = _minimum + (int)Math.Round(Math.Clamp(ratio, 0, 1) * (_maximum - _minimum));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var track = TrackBounds;
        var centre = KnobCentre;

        using(var rest = new SolidBrush(ListStyle.ProgressTrack))
            g.FillRectangle(rest, track);

        // The filled part reads as "how much", which is the whole point of a level control.
        using(var done = new SolidBrush(ListStyle.Highlight))
            g.FillRectangle(done, track.Left, track.Top, Math.Max(0, centre - track.Left), track.Height);

        var knob = new Rectangle(centre - KnobRadius, Height / 2 - KnobRadius, KnobRadius * 2, KnobRadius * 2);
        using(var fill = new SolidBrush(_hover || _dragging ? ListStyle.IconActive : ListStyle.Highlight))
            g.FillEllipse(fill, knob);
        using(var ring = new Pen(ListStyle.ListBackground, 2f))
            g.DrawEllipse(ring, knob);

        if(Focused)
        {
            using var focus = new Pen(ListStyle.IconActive) { DashStyle = DashStyle.Dot };
            g.DrawEllipse(focus, Rectangle.Inflate(knob, 3, 3));
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if(e.Button == MouseButtons.Left)
        {
            Focus();
            _dragging = true;
            SetValueFrom(e.X);
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if(_dragging) SetValueFrom(e.X);
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _dragging = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        Value += Math.Sign(e.Delta);
        base.OnMouseWheel(e);
    }

    // Without this the arrow keys are swallowed as navigation and never reach OnKeyDown.
    protected override bool IsInputKey(Keys key) => key switch
    {
        Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End => true,
        _ => base.IsInputKey(key),
    };

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch(e.KeyCode)
        {
            case Keys.Left or Keys.Down: Value -= 1; e.Handled = true; break;
            case Keys.Right or Keys.Up: Value += 1; e.Handled = true; break;
            case Keys.PageDown: Value -= 10; e.Handled = true; break;
            case Keys.PageUp: Value += 10; e.Handled = true; break;
            case Keys.Home: Value = _minimum; e.Handled = true; break;
            case Keys.End: Value = _maximum; e.Handled = true; break;
        }
        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        Invalidate();
        base.OnLostFocus(e);
    }
}
