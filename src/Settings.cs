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

    /// <summary>The pictures that can be shown behind the list, in the order they were added.</summary>
    public List<BackgroundImage> Backgrounds { get; set; } = [];

    /// <summary>Which of them is on screen. Out of range means the first one.</summary>
    public int BackgroundIndex { get; set; }

    /// <summary>"None", "Sequential" or "Random".</summary>
    public string BackgroundCycle { get; set; } = "None";

    /// <summary>How long each picture stays up when they are being cycled.</summary>
    public int BackgroundIntervalSeconds { get; set; } = 300;

    public const int MinBackgroundInterval = 5;
    public const int MaxBackgroundInterval = 86400;

    // --- the single-picture settings this replaced ------------------------
    // Read once at startup to carry an existing picture over, then left alone. They stay in the
    // file so that going back to an older build does not lose the picture.

    public bool BackgroundEnabled { get; set; }
    public double BackgroundFocusX { get; set; } = 0.5;
    public double BackgroundFocusY { get; set; } = 0.5;
    public double BackgroundOpacity { get; set; } = 0.18;

    /// <summary>
    /// How solid the cards and group bands are over a background picture, as a percentage.
    /// 100 is the plain look; lower lets more of the picture through. Only has an effect when a
    /// background picture is set, since there is nothing behind them otherwise.
    /// </summary>
    public int CardOpacity { get; set; } = 100;

    /// <summary>The range the setting is allowed to take. Below the floor the cards stop reading as cards.</summary>
    public const int MinCardOpacity = 20;
    public const int MaxCardOpacity = 100;

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
