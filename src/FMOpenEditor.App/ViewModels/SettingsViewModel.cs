using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FMOpenEditor.App.Services;
using FMOpenEditor.Core.Graphics;

namespace FMOpenEditor.App.ViewModels;

/// <summary>Appearance and graphics preferences, shown in the top-bar settings panel.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings = AppServices.Settings;
    private readonly Action _picturesChanged;

    [ObservableProperty] private int _themeIndex;
    [ObservableProperty] private bool _showPictures;
    [ObservableProperty] private bool _reduceMotion;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasCustomFolder))] private string? _graphicsFolder;
    [ObservableProperty] private string _folderSummary = "";
    [ObservableProperty] private string _librarySummary = "";

    public bool HasCustomFolder => !string.IsNullOrWhiteSpace(GraphicsFolder);

    /// <param name="picturesChanged">Called when pictures need re-indexing (toggle or folder change).</param>
    public SettingsViewModel(Action picturesChanged)
    {
        _picturesChanged = picturesChanged;
        _themeIndex = (int)_settings.Theme;
        _showPictures = _settings.ShowPictures;
        _reduceMotion = _settings.ReduceMotion;
        _graphicsFolder = _settings.GraphicsFolder;
        ImageService.Instance.Changed += Describe;
        Describe();
    }

    partial void OnThemeIndexChanged(int value)
    {
        if (value < 0) return;
        _settings.Theme = (ThemeChoice)value;
        _settings.Save();
        AppServices.ApplyTheme(_settings.Theme);
    }

    /// <summary>Follows a theme change made elsewhere (the top-bar toggle).</summary>
    public void SyncTheme() => ThemeIndex = (int)_settings.Theme;

    partial void OnShowPicturesChanged(bool value)
    {
        _settings.ShowPictures = value;
        _settings.Save();
        _picturesChanged();
        Describe();
    }

    partial void OnReduceMotionChanged(bool value)
    {
        _settings.ReduceMotion = value;
        _settings.Save();
        Motion.Set(!value);
    }

    partial void OnGraphicsFolderChanged(string? value)
    {
        _settings.GraphicsFolder = string.IsNullOrWhiteSpace(value) ? null : value;
        _settings.Save();
        _picturesChanged();
        Describe();
    }

    [RelayCommand] private void UseDetectedFolder() => GraphicsFolder = null;

    [RelayCommand] private void Rescan() => _picturesChanged();

    private void Describe()
    {
        var images = ImageService.Instance;
        var roots = images.Library.Roots.Count > 0 ? images.Library.Roots : GraphicsLocator.Find(null, _settings.GraphicsFolder);
        FolderSummary = roots.Count switch
        {
            0 when HasCustomFolder => "That folder doesn't exist. Pictures are hidden until it does.",
            0 => "No Football Manager graphics folder found. Names are shown on their own.",
            _ => string.Join(Environment.NewLine, roots),
        };

        var lib = images.Library;
        LibrarySummary = images.IsIndexing ? "Reading graphics packs…"
            : !_settings.ShowPictures ? "Pictures are switched off."
            : !images.Active ? "Pictures appear once you connect to a save."
            : lib.IsEmpty ? "No packs found in the graphics folder."
            : $"{lib.CountOf("person"):N0} faces · {lib.CountOf("club"):N0} club badges · " +
              $"{lib.CountOf("comp"):N0} competitions · {lib.CountOf("nation"):N0} nations, from {lib.PackCount:N0} packs";
    }
}
