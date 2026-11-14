using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FMOpenEditor.App.Services;
using FMOpenEditor.Core.Data;
using FMOpenEditor.Core.Definitions;
using FMOpenEditor.Core.Editing;
using FMOpenEditor.Core.Graphics;
using FMOpenEditor.Core.Memory;
using FMOpenEditor.Core.Model;
using FMOpenEditor.Core.Presets;

namespace FMOpenEditor.App.ViewModels;

/// <param name="Section">Heading shown above this entry when it starts a new group in the sidebar.</param>
public sealed record NavItem(string Title, string Icon, Func<object> CreatePage, string? Section = null);

public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly DispatcherTimer _watchTimer;
    private readonly Dictionary<NavItem, object> _pages = [];
    private IMemoryAccess? _liveMemory;
    private string? _liveModuleInfo;
    private nint _liveModuleBase;
    private long _liveModuleSize;
    private RelationIndex? _relations;

    /// <summary>A place in the app: a page and the entity open on it, if any.</summary>
    private sealed record Spot(NavItem Page, Entity? Entity);

    // Browser-style history: pages switched to, links followed and rows opened.
    private readonly Stack<Spot> _back = new();
    private readonly Stack<Spot> _forward = new();
    private Spot? _here;
    private DateTime _lastPick;
    private bool _restoring;

    public PresetLibrary Presets { get; }
    public DefinitionLibrary Definitions { get; }
    public ObservableCollection<NavItem> Nav { get; } = [];

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsConnected), nameof(ConnectionLabel))]
    private EditorSession? _session;
    [ObservableProperty] private NavItem? _selectedNav;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(GoBackCommand))] private bool _canGoBack;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(GoForwardCommand))] private bool _canGoForward;
    [ObservableProperty] private string? _backTip;
    [ObservableProperty] private string? _forwardTip;
    [ObservableProperty] private object? _currentPage;
    [ObservableProperty] private string _status = "Ready.";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasPending))] private int _pendingCount;
    [ObservableProperty] private bool _canUndo;
    [ObservableProperty] private bool _autoSave;
    [ObservableProperty] private string? _connectMessage;
    [ObservableProperty] private IReadOnlyList<string> _connectDetails = [];
    [ObservableProperty] private bool _isNight = true;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isIndexingGraphics;
    [ObservableProperty] private string? _graphicsStatus;

    private string? _edition;

    /// <summary>Appearance and graphics preferences (the gear in the top bar).</summary>
    public SettingsViewModel Settings { get; }

    /// <summary>Raised when the user asks to compare two entities; the view opens a window.</summary>
    public event Action<CompareViewModel>? CompareRequested;

    public bool IsConnected => Session is not null;
    public bool HasPending => PendingCount > 0;
    public string ConnectionLabel => Session?.Database.DisplayName ?? "Not connected";
    public string AppVersion => $"v{typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString(3)}";

    public MainWindowViewModel()
    {
        var baseDir = AppContext.BaseDirectory;
        Presets = new PresetLibrary(Path.Combine(baseDir, "presets"), PresetLibrary.DefaultUserFolder);
        Definitions = DefinitionLibrary.LoadFolder(Path.Combine(baseDir, "definitions"));

        _watchTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _watchTimer.Tick += (_, _) => Session?.Watcher.Tick();
        _watchTimer.Start();

        Settings = new SettingsViewModel(StartGraphics, OnCurrencyChanged);
        ImageService.Instance.Changed += OnGraphicsChanged;
        IsNight = AppServices.IsNight;
        if (Avalonia.Application.Current is { } app)
            app.ActualThemeVariantChanged += (_, _) => IsNight = AppServices.IsNight;

        ShowHome();
    }

    /// <summary>Flips between Day and Night from the top bar.</summary>
    [RelayCommand]
    private void ToggleTheme()
    {
        var settings = AppServices.Settings;
        settings.Theme = IsNight ? ThemeChoice.Day : ThemeChoice.Night;
        settings.Save();
        AppServices.ApplyTheme(settings.Theme);
        IsNight = AppServices.IsNight;
        Settings.SyncTheme();
    }

    /// <summary>
    /// (Re)indexes the FM graphics folder for the open save. Pictures only make sense for a real
    /// save, whose ids match the packs; otherwise the UI shows names alone.
    /// </summary>
    private void StartGraphics()
    {
        var images = ImageService.Instance;
        var settings = AppServices.Settings;
        var usable = Session is { } s && (s.Database.IsLive || AppServices.PicturesForDemo);
        if (!usable || !settings.ShowPictures)
        {
            images.Disable();
            return;
        }

        var roots = GraphicsLocator.Find(_edition, settings.GraphicsFolder);
        if (roots.Count == 0)
        {
            images.Disable();
            return;
        }
        _ = images.IndexAsync(roots);
    }

    /// <summary>Every amount on screen is re-shown in the currency just chosen in Settings.</summary>
    private void OnCurrencyChanged()
    {
        foreach (var p in _pages.Values.OfType<BrowserViewModel>()) p.CurrencyChanged();
        Status = $"Amounts are now shown in {Money.Current.Name}.";
    }

    /// <summary>The save's currencies with the game's own exchange rates (units per pound).</summary>
    private static List<CurrencyDisplay> ReadCurrencies(EditorSession session)
    {
        const EntityKind kind = EntityKind.Currency;
        if (!session.IsSupported(kind, "ExchangeRate")) return [];
        var list = new List<CurrencyDisplay>();
        foreach (var e in session.GetAll(kind))
        {
            if (session.TryRead(e, "ExchangeRate") is not double rate || !(rate > 0) || !double.IsFinite(rate)) continue;
            if (session.TryRead(e, "Name") is not string { Length: > 0 } name) continue;
            string Text(string key) => session.IsSupported(kind, key) ? session.TryRead(e, key) as string ?? "" : "";
            list.Add(new CurrencyDisplay(e.Id, name, Text("Symbol"), Text("ShortName"), rate));
        }
        return list;
    }

    private void OnGraphicsChanged()
    {
        var images = ImageService.Instance;
        IsIndexingGraphics = images.IsIndexing;
        GraphicsStatus = images.IsIndexing ? "Reading graphics packs…"
            : images.Active && !images.Library.IsEmpty ? $"{images.Library.Count:N0} pictures" : null;
    }

    private void ShowHome()
    {
        Nav.Clear();
        _pages.Clear();
        CurrentPage = this; // the home page binds straight to this view model
    }

    partial void OnSelectedNavChanged(NavItem? value)
    {
        if (value is null) return;
        if (!_pages.TryGetValue(value, out var page)) _pages[value] = page = value.CreatePage();
        CurrentPage = page;
        if (!_restoring) Visit(new Spot(value, (page as BrowserViewModel)?.Editor?.Entity));
    }

    partial void OnAutoSaveChanged(bool value)
    {
        if (Session is null) return;
        Session.AutoSave = value;
        if (value && Session.Changes.Count > 0) Save();
    }

    [RelayCommand]
    private void OpenDemo() => Open(new DemoDatabase(), null, null);

    [RelayCommand]
    private async Task ConnectGame()
    {
        ConnectDetails = [];
        ConnectMessage = null;
        if (!OperatingSystem.IsWindows())
        {
            ConnectMessage = "Connecting to the game is only supported on Windows for now.";
            return;
        }

        IsBusy = true;
        try
        {
            // Finding the process and walking the game's lists can take a moment on a big save;
            // doing it off the UI thread keeps the loading animation moving.
            var game = await Task.Run(() => GameProcess.FindRunning());
            if (game is null)
            {
                ConnectMessage = "Football Manager isn't running. Start the game and load your save, then try again. " +
                                 "If the game is running as administrator, run FM OpenEditor as administrator too.";
                return;
            }

            var def = Definitions.Match(game.ModuleVersion);
            if (def is null)
            {
                ConnectMessage = $"No game definition matches FM build {game.ModuleVersion}. " +
                                 "Copy definitions/_template.json to start mapping it (docs/DEFINITIONS.md).";
                return;
            }

            var (memory, db) = await Task.Run<(ProcessMemory, LiveDatabase)>(() =>
            {
                var m = new ProcessMemory(game.ProcessId);
                try
                {
                    return (m, new LiveDatabase(m, def, game.ModuleBase, game.ModuleSize,
                        $"FM {def.Version} · live (build {game.ModuleVersion})", owner: m));
                }
                catch
                {
                    m.Dispose();
                    throw;
                }
            });
            _edition = def.Version.Split('.')[0];

            var mapped = def.Structs.Values.Sum(f => f.Values.Count(x => x.IsMapped));
            var total = def.Structs.Values.Sum(f => f.Count);
            var details = new List<string>
            {
                $"Definition: {Path.GetFileName(def.SourcePath)} — {mapped} of {total} fields mapped{(def.Verified ? "" : ", not yet verified")}.",
            };
            details.AddRange(db.Warnings);
            ConnectDetails = details;

            var info = $"{game.ProcessName}.exe (pid {game.ProcessId}) · {def.Module} at 0x{game.ModuleBase:X}, {game.ModuleSize / 1048576} MB";
            Open(db, memory, info, game.ModuleBase, game.ModuleSize);
            ConnectMessage = db.GetAll(EntityKind.Player).Count == 0
                ? "Attached, but no players were found: this game version isn't mapped yet. The Research tools are ready for mapping it."
                : null;
        }
        catch (Exception ex)
        {
            ConnectMessage = $"Couldn't attach to the game: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Open(IGameDatabase db, IMemoryAccess? memory, string? moduleInfo, nint moduleBase = 0, long moduleSize = 0)
    {
        Disconnect();
        _liveMemory = memory;
        _liveModuleInfo = moduleInfo;
        _liveModuleBase = moduleBase;
        _liveModuleSize = moduleSize;
        var session = new EditorSession(db) { AutoSave = AutoSave };
        session.Changes.Changed += UpdatePending;
        Session = session;
        _relations = new RelationIndex(session);
        Settings.UseCurrencies(ReadCurrencies(session), db.GameCurrencyId);
        ClearHistory();
        if (!db.IsLive) _edition = null;
        StartGraphics();

        Nav.Clear();
        _pages.Clear();
        // Your own club first: the club of each manager the people playing this save control.
        var relations = _relations!;
        var myClubs = db.HumanManagers.Select(h => session.TryRead(h, "Contract.Club")).OfType<int>().Distinct()
            .Select(id => db.Find(EntityKind.Club, id)).OfType<Entity>().ToList();
        foreach (var club in myClubs)
        {
            var c = club;
            var name = session.NameOf(EntityKind.Club, c.Id);
            // With several human clubs (a network or multi-manager save), say whose squad each one is.
            var squad = myClubs.Count > 1 ? $"Squad · {name}" : "Squad";
            var staff = myClubs.Count > 1 ? $"Club staff · {name}" : "Club staff";
            Nav.Add(new NavItem(name, "club", () => NewBrowser(EntityKind.Club, () => [c]), Nav.Count == 0 ? "MY CLUB" : null));
            Nav.Add(new NavItem(squad, "player",
                () => NewBrowser(EntityKind.Player, () => relations.Find(EntityKind.Player, "Contract.Club", c.Id).ToList(), squad)));
            Nav.Add(new NavItem(staff, "staff",
                () => NewBrowser(EntityKind.Staff, () => relations.Find(EntityKind.Staff, "Contract.Club", c.Id).ToList(), staff)));
        }
        // Grouped so the record types read as four short lists. A type is listed only when this
        // game version has records for it (the demo database has just the first five).
        // Teams have no page: people look for clubs, and a club's page carries its first team's fields.
        (string Section, EntityKind[] Kinds)[] groups =
        [
            ("PEOPLE", [EntityKind.Player, EntityKind.Staff]),
            ("FOOTBALL", [EntityKind.Club, EntityKind.Competition, EntityKind.Stadium, EntityKind.Award, EntityKind.Derby]),
            ("WORLD", [EntityKind.Nation, EntityKind.Continent, EntityKind.City, EntityKind.LocalRegion, EntityKind.Language, EntityKind.Currency, EntityKind.Weather]),
            ("GAME DATA", [EntityKind.Injury, EntityKind.MediaSource, EntityKind.StageName]),
        ];
        foreach (var (section, kinds) in groups)
        {
            var first = true;
            foreach (var kind in kinds.Where(k => k <= EntityKind.Nation || db.GetAll(k).Count > 0))
            {
                var k = kind;
                Nav.Add(new NavItem(PropertyCatalog.Plural(k), KindIcon(k), () => NewBrowser(k), first ? section : null));
                first = false;
            }
        }
        Nav.Add(new NavItem("Watch list", "snow", () => new WatchListViewModel(session), "TOOLS"));
        Nav.Add(new NavItem("Research", "flask", () => new ResearchViewModel(_liveMemory, _liveModuleInfo, _liveModuleBase, _liveModuleSize)));
        SelectedNav = Nav[0];
        UpdatePending();
        Status = $"Opened {db.DisplayName}: {db.GetAll(EntityKind.Player).Count:N0} players, {db.GetAll(EntityKind.Club).Count:N0} clubs.";
    }

    private BrowserViewModel NewBrowser(EntityKind kind, Func<IReadOnlyList<Entity>>? source = null, string? title = null)
    {
        var browser = new BrowserViewModel(Session!, kind, Presets, s => Status = s,
            (a, b) => CompareRequested?.Invoke(new CompareViewModel(Session!, a, b)), Navigate, _relations, source, title);
        // Opening a row is a step in the history too, so Back returns to the previous player.
        browser.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(BrowserViewModel.Editor) || _restoring) return;
            if (!ReferenceEquals(CurrentPage, browser) || SelectedNav is not { } page || browser.Editor?.Entity is not { } entity) return;
            Visit(new Spot(page, entity), pick: true);
        };
        return browser;
    }

    private static string KindIcon(EntityKind kind) => kind switch
    {
        EntityKind.Player => "player",
        EntityKind.Staff => "staff",
        EntityKind.Club or EntityKind.Team or EntityKind.Derby => "club",
        EntityKind.Competition or EntityKind.Award or EntityKind.StageName => "trophy",
        EntityKind.Nation or EntityKind.Stadium or EntityKind.City or EntityKind.LocalRegion or EntityKind.Continent => "globe",
        EntityKind.Injury => "bolt",
        EntityKind.Weather => "sun",
        _ => "folder",
    };

    /// <summary>Follows a link: switches to the page for <paramref name="entity"/>'s kind and opens it there.</summary>
    public void Navigate(Entity entity)
    {
        // A team (a derby's two sides, say) opens as the club it belongs to.
        if (entity.Kind == EntityKind.Team && Session is { } s)
        {
            if (s.TryRead(entity, "Club") is not int clubId || s.Database.Find(EntityKind.Club, clubId) is not { } club) return;
            entity = club;
        }
        var target = Nav.FirstOrDefault(n => n.Title == PropertyCatalog.Plural(entity.Kind));
        if (target is null) return;
        Show(new Spot(target, entity));
        Visit(new Spot(target, entity));
    }

    /// <summary>Back one step: the previous page, player, club…</summary>
    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack() => Step(_back, _forward);

    /// <summary>Forward again after going back.</summary>
    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void GoForward() => Step(_forward, _back);

    private void Step(Stack<Spot> from, Stack<Spot> to)
    {
        if (!from.TryPop(out var spot)) return;
        if (_here is { } here) to.Push(here);
        _here = spot;
        Show(spot);
        UpdateHistory();
    }

    /// <summary>Puts a spot on screen without recording it.</summary>
    private void Show(Spot spot)
    {
        _restoring = true;
        try
        {
            SelectedNav = spot.Page;
            if (spot.Entity is { } e) (CurrentPage as BrowserViewModel)?.Open(e);
        }
        finally
        {
            _restoring = false;
        }
    }

    /// <summary>
    /// Records arriving at <paramref name="spot"/>. Rows picked in quick succession (arrowing down the
    /// list) count as one step, and the first pick on a page replaces the empty page.
    /// </summary>
    private void Visit(Spot spot, bool pick = false)
    {
        var now = DateTime.UtcNow;
        if (_here is { } here)
        {
            if (here == spot) return;
            var samePage = here.Page == spot.Page;
            var replace = samePage && (here.Entity is null || pick && now - _lastPick < TimeSpan.FromMilliseconds(700));
            if (!replace) _back.Push(here);
            _forward.Clear();
        }
        _here = spot;
        if (pick) _lastPick = now;
        UpdateHistory();
    }

    private string Describe(Spot spot) =>
        spot.Entity is { } e && Session is { } s ? s.NameOf(e.Kind, e.Id) : spot.Page.Title;

    private void UpdateHistory()
    {
        CanGoBack = _back.Count > 0;
        CanGoForward = _forward.Count > 0;
        BackTip = _back.TryPeek(out var b) ? $"Back to {Describe(b)} (mouse back button, Alt+Left)" : "Back";
        ForwardTip = _forward.TryPeek(out var f) ? $"Forward to {Describe(f)} (mouse forward button, Alt+Right)" : "Forward";
    }

    private void ClearHistory()
    {
        _back.Clear();
        _forward.Clear();
        _here = null;
        UpdateHistory();
    }

    private void UpdatePending()
    {
        PendingCount = Session?.Changes.Count ?? 0;
        CanUndo = Session?.Changes.CanUndo ?? false;
    }

    [RelayCommand]
    private void Save()
    {
        if (Session is null) return;
        var n = Session.Changes.Count;
        var failed = Session.Save();
        Status = failed.Count == 0
            ? $"Saved {n} change(s) to {(Session.Database.IsLive ? "the game" : "the demo save")}."
            : $"Saved {n - failed.Count}, {failed.Count} failed: {failed[0].Error.Message}";
    }

    [RelayCommand]
    private void Discard()
    {
        Session?.Discard();
        Status = "Pending changes discarded.";
    }

    [RelayCommand]
    private void Undo()
    {
        if (Session is null) return;
        var n = Session.UndoLastSave();
        Status = n > 0 ? $"Undid the last save ({n} value(s) restored)." : "Nothing to undo.";
    }

    [RelayCommand]
    private void Refresh()
    {
        Session?.Refresh();
        foreach (var p in _pages.Values.OfType<BrowserViewModel>()) p.SearchCommand.Execute(null);
        Status = "Re-read the game data.";
    }

    [RelayCommand]
    private void Disconnect()
    {
        foreach (var p in _pages.Values)
        {
            if (p is BrowserViewModel b) b.Detach();
            if (p is WatchListViewModel w) w.Detach();
        }
        if (Session is { } s)
        {
            s.Changes.Changed -= UpdatePending;
            s.Dispose();
        }
        Session = null;
        _relations = null;
        Settings.UseCurrencies([]);
        ClearHistory();
        ImageService.Instance.Disable();
        _liveMemory = null;
        SelectedNav = null;
        UpdatePending();
        ShowHome();
    }

    public void Dispose()
    {
        _watchTimer.Stop();
        Session?.Dispose();
    }
}
