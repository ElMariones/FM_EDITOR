using Avalonia;
using Avalonia.Styling;

namespace FMOpenEditor.App.Services;

/// <summary>App-wide state that isn't tied to a save: preferences and the theme.</summary>
public static class AppServices
{
    private static AppSettings? _settings;

    /// <summary>The user's preferences. Tests replace this with an in-memory instance.</summary>
    public static AppSettings Settings
    {
        get => _settings ??= AppSettings.Load(AppSettings.DefaultPath);
        set => _settings = value;
    }

    /// <summary>Test hook: let the demo save (whose ids are invented) look pictures up too.</summary>
    public static bool PicturesForDemo { get; set; }

    public static void ApplyTheme(ThemeChoice choice)
    {
        if (Application.Current is not { } app) return;
        app.RequestedThemeVariant = choice switch
        {
            ThemeChoice.Day => ThemeVariant.Light,
            ThemeChoice.Night => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }

    public static bool IsNight => Application.Current?.ActualThemeVariant != ThemeVariant.Light;
}
