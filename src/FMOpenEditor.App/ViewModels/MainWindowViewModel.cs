using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FMOpenEditor.Core.Data;
using FMOpenEditor.Core.Definitions;
using FMOpenEditor.Core.Editing;
using FMOpenEditor.Core.Memory;
using FMOpenEditor.Core.Model;
using FMOpenEditor.Core.Presets;

namespace FMOpenEditor.App.ViewModels;

public sealed record NavItem(string Title, string Icon, Func<object> CreatePage);

public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly DispatcherTimer _watchTimer;
    private readonly Dictionary<NavItem, object> _pages = [];
    private IMemoryAccess? _liveMemory;
    private string? _liveModuleInfo;

    public PresetLibrary Presets { get; }
    public DefinitionLibrary Definitions { get; }
    public ObservableCollection<NavItem> Nav { get; } = [];

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsConnected), nameof(ConnectionLabel))]
    private EditorSession? _session;
    [ObservableProperty] private NavItem? _selectedNav;
    [ObservableProperty] private object? _currentPage;
    [ObservableProperty] private string _status = "Ready.";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasPending))] private int _pendingCount;
    [ObservableProperty] private bool _canUndo;
    [ObservableProperty] private bool _autoSave;
    [ObservableProperty] private string? _connectMessage;
    [ObservableProperty] private IReadOnlyList<string> _connectDetails = [];

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

        ShowHome();
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
    private void ConnectGame()
    {
        ConnectDetails = [];
        if (!OperatingSystem.IsWindows())
        {
            ConnectMessage = "Connecting to the game is only supported on Windows for now.";
            return;
        }

        var game = GameProcess.FindRunning();
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

        try
        {
            var memory = new ProcessMemory(game.ProcessId);
            var db = new LiveDatabase(memory, def, game.ModuleBase, game.ModuleSize,
                $"FM {def.Version} · live (build {game.ModuleVersion})", owner: memory);

            var mapped = def.Structs.Values.Sum(f => f.Values.Count(x => x.IsMapped));
            var total = def.Structs.Values.Sum(f => f.Count);
            var details = new List<string>
            {
                $"Definition: {Path.GetFileName(def.SourcePath)} — {mapped} of {total} fields mapped{(def.Verified ? "" : ", not yet verified")}.",
            };
            details.AddRange(db.Warnings);
            ConnectDetails = details;

            var info = $"{game.ProcessName}.exe (pid {game.ProcessId}) · {def.Module} at 0x{game.ModuleBase:X}, {game.ModuleSize / 1048576} MB";
            Open(db, memory, info);
            ConnectMessage = db.GetAll(EntityKind.Player).Count == 0
                ? "Attached, but no players were found: this game version isn't mapped yet. The Research tools are ready for mapping it."
                : null;
        }
        catch (Exception ex)
        {
            ConnectMessage = $"Couldn't attach to the game: {ex.Message}";
        }
    }

    private void Open(IGameDatabase db, IMemoryAccess? memory, string? moduleInfo)
    {
        Disconnect();
        _liveMemory = memory;
        _liveModuleInfo = moduleInfo;
        var session = new EditorSession(db) { AutoSave = AutoSave };
        session.Changes.Changed += UpdatePending;
        Session = session;

        Nav.Clear();
        _pages.Clear();
        Nav.Add(new NavItem("Players", "⚽", () => NewBrowser(EntityKind.Player)));
        Nav.Add(new NavItem("Staff", "📋", () => NewBrowser(EntityKind.Staff)));
        Nav.Add(new NavItem("Clubs", "🏟", () => NewBrowser(EntityKind.Club)));
        Nav.Add(new NavItem("Competitions", "🏆", () => NewBrowser(EntityKind.Competition)));
        Nav.Add(new NavItem("Nations", "🌍", () => NewBrowser(EntityKind.Nation)));
        Nav.Add(new NavItem("Watch list", "❄", () => new WatchListViewModel(session)));
        Nav.Add(new NavItem("Research", "🔬", () => new ResearchViewModel(_liveMemory, _liveModuleInfo)));
        SelectedNav = Nav[0];
        UpdatePending();
        Status = $"Opened {db.DisplayName}: {db.GetAll(EntityKind.Player).Count:N0} players, {db.GetAll(EntityKind.Club).Count:N0} clubs.";
    }

    private BrowserViewModel NewBrowser(EntityKind kind) =>
        new(Session!, kind, Presets, s => Status = s, (a, b) => CompareRequested?.Invoke(new CompareViewModel(Session!, a, b)));

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
