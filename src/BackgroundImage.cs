namespace OtpManager;

/// <summary>
/// One picture in the background library. The file lives beside the accounts; everything else here
/// is how that picture is framed, and belongs to it rather than to the application - two pictures
/// rarely want the same crop or the same strength.
/// </summary>
internal sealed class BackgroundImage
{
    /// <summary>File name inside the background folder. Not a path, so the folder can move.</summary>
    public string File { get; set; } = "";

    /// <summary>The point to keep in view, as a fraction of the picture's width and height.</summary>
    public double FocusX { get; set; } = 0.5;
    public double FocusY { get; set; } = 0.5;

    /// <summary>How strongly this picture shows through, 0 to 1.</summary>
    public double Opacity { get; set; } = 0.18;
}
