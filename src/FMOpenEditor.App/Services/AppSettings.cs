using System.Text.Json;

namespace FMOpenEditor.App.Services;

public enum ThemeChoice
{
    System,
    Day,
    Night,
}

/// <summary>User preferences, kept as JSON next to the user's presets.</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public ThemeChoice Theme { get; set; } = ThemeChoice.Night;

    /// <summary>Show faces and badges from the FM graphics folder.</summary>
    public bool ShowPictures { get; set; } = true;

    /// <summary>A graphics folder chosen by hand; null means find it automatically.</summary>
    public string? GraphicsFolder { get; set; }

    /// <summary>Turns off decorative animation (background drift, entrances, the mascot).</summary>
    public bool ReduceMotion { get; set; }

    /// <summary>Tiki's tour runs once, the first time a save is connected. The ? button replays it.</summary>
    public bool TourWorkspaceSeen { get; set; }

    /// <summary>The game's id of the currency amounts are shown in (28 euro, 86 pound, 87 US dollar).</summary>
    public int CurrencyId { get; set; } = 28;

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FMOpenEditor", "settings.json");

    /// <summary>Where this instance saves; null keeps it in memory only (tests).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? FilePath { get; private set; }

    public static AppSettings Load(string? path)
    {
        AppSettings? settings = null;
        try
        {
            if (path is not null && File.Exists(path))
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A damaged settings file falls back to defaults rather than blocking start-up.
        }
        settings ??= new AppSettings();
        settings.FilePath = path;
        return settings;
    }

    public void Save()
    {
        if (FilePath is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Preferences are a convenience; failing to persist them must not break editing.
        }
    }
}
