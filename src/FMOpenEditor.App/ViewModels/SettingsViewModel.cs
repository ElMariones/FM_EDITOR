using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FMOpenEditor.App.Services;
using FMOpenEditor.Core.Graphics;
using FMOpenEditor.Core.Model;
using FMOpenEditor.Core.Search;

namespace FMOpenEditor.App.ViewModels;

/// <summary>A currency in the picker: "€  Euro" on the quick buttons, the full name in the list.</summary>
public sealed record CurrencyOption(CurrencyDisplay Currency)
{
    public string Short => Currency.Symbol.Length > 0 ? $"{Currency.Symbol}  {Currency.ShortName}" : Currency.ShortName;
    public string Mark => Currency.Symbol.Length > 0 ? Currency.Symbol : Currency.ShortName;
    public string Name => Currency.Name;
}

/// <summary>Appearance, currency and graphics preferences, shown in the top-bar settings panel.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings = AppServices.Settings;
    private readonly Action _picturesChanged;
    private readonly Action? _currencyChanged;
    private IReadOnlyList<CurrencyOption> _allCurrencies = [];
    private bool _syncingCurrency;

    [ObservableProperty] private int _themeIndex;
    [ObservableProperty] private bool _showPictures;
    [ObservableProperty] private bool _reduceMotion;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasCustomFolder))] private string? _graphicsFolder;
    [ObservableProperty] private string _folderSummary = "";
    [ObservableProperty] private string _librarySummary = "";

    /// <summary>Euro, pound and dollar: one click each.</summary>
    [ObservableProperty] private IReadOnlyList<CurrencyOption> _commonCurrencies = Money.Common.Select(c => new CurrencyOption(c)).ToList();
    [ObservableProperty] private CurrencyOption? _selectedCommon;

    /// <summary>The rest of the save's currencies, behind "More" and a search box.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(MoreCurrenciesLabel))] private bool _showMoreCurrencies;
    [ObservableProperty] private string? _currencySearch;
    [ObservableProperty] private IReadOnlyList<CurrencyOption> _currencyResults = [];
    [ObservableProperty] private CurrencyOption? _selectedOther;
    [ObservableProperty] private string _currencySummary = "";
    [ObservableProperty] private string? _currencyHint;

    public string MoreCurrenciesLabel => ShowMoreCurrencies ? "Less" : "More…";

    public bool HasCustomFolder => !string.IsNullOrWhiteSpace(GraphicsFolder);

    /// <param name="picturesChanged">Called when pictures need re-indexing (toggle or folder change).</param>
    /// <param name="currencyChanged">Called when amounts on screen need showing in another currency.</param>
    public SettingsViewModel(Action picturesChanged, Action? currencyChanged = null)
    {
        _picturesChanged = picturesChanged;
        _currencyChanged = currencyChanged;
        UseCurrencies([]);
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

    /// <summary>
    /// Takes the currencies of the save that was just opened (with the game's own exchange rates), or
    /// none when no save is open, and switches to the game's own currency or else the user's saved choice.
    /// </summary>
    public void UseCurrencies(IReadOnlyList<CurrencyDisplay> fromSave, int? gameCurrencyId = null)
    {
        if (gameCurrencyId is { } id && fromSave.Any(c => c.Id == id))
        {
            _settings.CurrencyId = id;
            _settings.Save();
        }
        _allCurrencies = fromSave.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).Select(c => new CurrencyOption(c)).ToList();
        // The quick three use the save's rate when it has one.
        CommonCurrencies = Money.Common
            .Select(c => new CurrencyOption(fromSave.FirstOrDefault(s => s.Id == c.Id) ?? c)).ToList();
        var wanted = fromSave.FirstOrDefault(c => c.Id == _settings.CurrencyId)
                     ?? CommonCurrencies.FirstOrDefault(c => c.Currency.Id == _settings.CurrencyId)?.Currency
                     ?? Money.Pound;
        Money.Current = wanted;
        CurrencyHint = fromSave.Count == 0 ? "Connect to a save to choose from every currency in it." : null;
        SyncCurrency();
    }

    partial void OnSelectedCommonChanged(CurrencyOption? value)
    {
        if (!_syncingCurrency && value is not null) ChooseCurrency(value.Currency);
    }

    partial void OnSelectedOtherChanged(CurrencyOption? value)
    {
        if (!_syncingCurrency && value is not null) ChooseCurrency(value.Currency);
    }

    partial void OnCurrencySearchChanged(string? value) => FilterCurrencies();

    [RelayCommand] private void ToggleMoreCurrencies() => ShowMoreCurrencies = !ShowMoreCurrencies;

    private void ChooseCurrency(CurrencyDisplay currency)
    {
        if (currency == Money.Current) return;
        Money.Current = currency;
        _settings.CurrencyId = currency.Id;
        _settings.Save();
        SyncCurrency();
        _currencyChanged?.Invoke();
    }

    private void FilterCurrencies()
    {
        var wanted = SearchEngine.Normalize(CurrencySearch ?? "");
        var found = _allCurrencies
            .Where(c => wanted.Length == 0 || SearchEngine.Normalize($"{c.Name} {c.Currency.ShortName} {c.Currency.Symbol}").Contains(wanted))
            .ToList();
        _syncingCurrency = true;
        try
        {
            CurrencyResults = found;
            SelectedOther = found.FirstOrDefault(c => c.Currency.Id == Money.Current.Id);
        }
        finally { _syncingCurrency = false; }
    }

    /// <summary>Makes the buttons, the list and the summary line show the current currency.</summary>
    private void SyncCurrency()
    {
        var current = Money.Current;
        _syncingCurrency = true;
        try
        {
            SelectedCommon = CommonCurrencies.FirstOrDefault(c => c.Currency.Id == current.Id);
        }
        finally { _syncingCurrency = false; }
        FilterCurrencies();
        var mark = new CurrencyOption(current).Mark;
        CurrencySummary = current.Id == Money.Pound.Id
            ? $"Amounts are shown in {current.Name}, the currency the game stores them in."
            : $"Amounts are shown in {current.Name}. £1 = {current.Rate.ToString(current.Rate >= 100 ? "N0" : "0.##", System.Globalization.CultureInfo.InvariantCulture)} {mark}, " +
              (_allCurrencies.Count > 0 ? "the rate in your save." : "an approximate rate until a save is open.");
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
