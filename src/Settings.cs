using System.Text;
using System.Text.Json;

namespace OtpManager;

/// <summary>Application settings, kept beside the accounts but deliberately separate from them.</summary>
internal sealed class Settings
{
    /// <summary>
    /// Difference between this machine's clock and real time, in seconds. Codes are generated
    /// against the corrected time; the system clock itself is never touched.
    /// </summary>
    public int TimeOffsetSeconds { get; set; }

    /// <summary>When the offset above was last measured, as a round-trip ISO string.</summary>
    public string TimeCheckedUtc { get; set; } = "";

    /// <summary>Whether the chosen picture is drawn behind the list.</summary>
    public bool BackgroundEnabled { get; set; }

    /// <summary>The point of the picture to keep in view, as a fraction of its width and height.</summary>
    public double BackgroundFocusX { get; set; } = 0.5;
    public double BackgroundFocusY { get; set; } = 0.5;

    /// <summary>How strongly the picture shows through, 0 to 1.</summary>
    public double BackgroundOpacity { get; set; } = 0.18;

    /// <summary>"System", "Light" or "Dark".</summary>
    public string Theme { get; set; } = "System";

    private static string FilePath => Path.Combine(AccountStore.Directory, "settings.json");

    public static Settings Current { get; private set; } = Load();

    private static Settings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath, Encoding.UTF8)) ?? new Settings()
                : new Settings();
        }
        catch(Exception)
        {
            // Settings are conveniences; a damaged file must not stop the app from starting.
            return new Settings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AccountStore.Directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        }
        catch(Exception)
        {
        }
    }

    /// <summary>Unix milliseconds with the measured clock error taken out.</summary>
    public long NowUnixMilliseconds() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + TimeOffsetSeconds * 1000L;
}
