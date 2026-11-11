using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FMOpenEditor.Core.Editing;
using FMOpenEditor.Core.Model;

namespace FMOpenEditor.App.ViewModels;

/// <summary>One club in the picker: badge, name and where it plays.</summary>
public sealed class ClubOption(Entity club, string name, string detail, bool isCurrent)
{
    public Entity Club { get; } = club;
    public string Name { get; } = name;
    /// <summary>League and nation, e.g. "Bundesliga · Germany".</summary>
    public string Detail { get; } = detail;
    public bool IsCurrent { get; } = isCurrent;
    public EntityPicture Badge { get; } = new(club, FMOpenEditor.Core.Graphics.GraphicSize.Small);
    public override string ToString() => Name;
}

/// <summary>The Transfer window: pick a club, then the fee, the date, the wage and the contract.</summary>
public sealed partial class TransferViewModel : ObservableObject
{
    private const int MaxResults = 60;

    private readonly EditorSession _session;
    private readonly Action<string> _status;
    private List<(Entity Club, string Name, int Reputation)>? _clubs;
    private readonly int? _currentClubId;

    public Entity Player { get; }
    public string PlayerName { get; }
    public string CurrentClub { get; }
    public string CurrentWageText { get; }
    public string CurrencySymbol => Money.Current.Symbol;

    /// <summary>The player's face, when the user's graphics have one.</summary>
    public EntityPicture Picture { get; }

    public ObservableCollection<ClubOption> Results { get; } = [];

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SearchHint))] private string? _clubSearch;

    /// <summary>The club chosen to move to.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasClub), nameof(Problem), nameof(HasProblem), nameof(Summary), nameof(CanCancelPicking), nameof(ShowKeepChoice))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private ClubOption? _selectedClub;

    /// <summary>The row under the keyboard cursor in the results; Enter chooses it.</summary>
    [ObservableProperty] private ClubOption? _highlighted;

    /// <summary>True while the search is showing; false once a club is chosen and shown as a card.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsChosen), nameof(ShowKeepChoice))] private bool _isPicking = true;

    public bool IsChosen => !IsPicking;
    public bool CanCancelPicking => SelectedClub is not null;
    /// <summary>"Keep current choice": only while searching again after a club was chosen.</summary>
    public bool ShowKeepChoice => IsPicking && SelectedClub is not null;

    /// <summary>Shown under the search box when there is nothing to list.</summary>
    public string? SearchHint => (ClubSearch?.Trim().Length ?? 0) switch
    {
        0 => "Type at least two letters of the club's name.",
        1 => "Keep typing…",
        _ => Results.Count == 0 ? "No club by that name." : null,
    };

    /// <summary>Fee in the currency chosen in Settings.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Problem), nameof(HasProblem), nameof(Summary))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private decimal? _fee = 0;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsFuture), nameof(Problem), nameof(HasProblem), nameof(Summary), nameof(FutureNote))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private bool _isNow = true;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Problem), nameof(HasProblem), nameof(Summary))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private DateTime? _date;

    /// <summary>Weekly wage at the new club, in the currency chosen in Settings. Starts at the current wage.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Problem), nameof(HasProblem))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private decimal? _wage;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Problem), nameof(HasProblem))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private DateTime? _contractExpiry;

    [ObservableProperty] private bool _moveMoney = true;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasScheduled))] private string? _scheduledText;

    public bool IsFuture
    {
        get => !IsNow;
        set => IsNow = !value;
    }

    public bool HasClub => SelectedClub is not null;
    public bool HasResults => Results.Count > 0;
    public bool HasSearchHint => SearchHint is not null;
    public bool HasScheduled => ScheduledText is not null;
    public bool CanEditWage => _session.CanEdit(EntityKind.Player, "Contract.Wage");
    public bool CanEditExpiry => _session.CanEdit(EntityKind.Player, "Contract.Expiry");
    public bool CanMoveMoney => _session.CanEdit(EntityKind.Club, "Finance.Balance");
    public bool IsLive => _session.Database.IsLive;
    public DateTime GameDate => _session.GameDate.ToDateTime(TimeOnly.MinValue);

    public string FutureNote => _session.Transfers.CanBookInGame
        ? "The game will make the move on that date."
        : "The editor makes the move when the game reaches that date, so keep it open and connected until then. "
          + "If the game moves him to another club first, the game's move stands and this one is cancelled.";

    /// <summary>Raised when the window should close.</summary>
    public event Action? CloseRequested;

    public TransferViewModel(EditorSession session, Entity player, Action<string> status)
    {
        _session = session;
        _status = status;
        Player = player;
        PlayerName = session.Format(player, PropertyCatalog.DisplayName);
        _currentClubId = session.IsSupported(player.Kind, "Contract.Club") ? session.TryRead(player, "Contract.Club") as int? : null;
        CurrentClub = _currentClubId is { } id ? session.NameOf(EntityKind.Club, id) : "No club";
        Picture = new EntityPicture(player, FMOpenEditor.Core.Graphics.GraphicSize.Large);

        var wage = session.TryRead(player, "Contract.Wage") is { } w ? (long)Values.ToDouble(w) : 0;
        CurrentWageText = Values.FormatMoney(wage);
        _wage = (decimal)Math.Round(Money.ToDisplay(wage));

        var game = session.GameDate;
        _date = game.AddMonths(1).ToDateTime(TimeOnly.MinValue);
        var expiry = session.TryRead(player, "Contract.Expiry") as DateOnly?;
        _contractExpiry = (expiry is { } e && e > game.AddMonths(6) ? e : game.AddYears(3)).ToDateTime(TimeOnly.MinValue);
        RefreshScheduled();
        Results.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasResults));
            OnPropertyChanged(nameof(SearchHint));
            OnPropertyChanged(nameof(HasSearchHint));
        };
    }

    partial void OnClubSearchChanged(string? value)
    {
        Search(value);
        OnPropertyChanged(nameof(HasSearchHint));
    }

    private void Search(string? text)
    {
        Results.Clear();
        text = text?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length < 2) return;
        _clubs ??= LoadClubs();
        // The best-known clubs first: the save also holds placeholder copies of big clubs with no squad,
        // and those have next to no reputation.
        foreach (var (club, name, _) in _clubs
                     .Where(c => c.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase))
                     .OrderByDescending(c => c.Reputation)
                     .ThenBy(c => c.Name.StartsWith(text, StringComparison.CurrentCultureIgnoreCase) ? 0 : 1)
                     .ThenBy(c => c.Name.Length)
                     .Take(MaxResults))
        {
            Results.Add(new ClubOption(club, name, DetailOf(club), club.Id == _currentClubId));
        }
        Highlighted = Results.FirstOrDefault();
    }

    private List<(Entity, string, int)> LoadClubs()
    {
        var reputation = _session.IsSupported(EntityKind.Club, "Reputation");
        return _session.GetAll(EntityKind.Club)
            .Select(c => (c, _session.NameOf(EntityKind.Club, c.Id),
                          reputation && _session.TryRead(c, "Reputation") is { } r ? (int)Values.ToDouble(r) : 0))
            .Where(c => c.Item2.Length > 0 && !c.Item2.StartsWith('#'))
            .ToList();
    }

    private string DetailOf(Entity club)
    {
        string Ref(string key, EntityKind kind) =>
            _session.IsSupported(EntityKind.Club, key) && _session.TryRead(club, key) is int id ? _session.NameOf(kind, id) : "";
        var parts = new[] { Ref("League", EntityKind.Competition), Ref("Nation", EntityKind.Nation) };
        return string.Join("  ·  ", parts.Where(p => p.Length > 0 && !p.StartsWith('#')));
    }

    /// <summary>Picks a club from the results and folds the search away.</summary>
    [RelayCommand]
    private void Choose(ClubOption? option)
    {
        option ??= Highlighted;
        if (option is null) return;
        SelectedClub = option;
        IsPicking = false;
    }

    /// <summary>Opens the search again to pick a different club.</summary>
    [RelayCommand]
    private void ChangeClub() => IsPicking = true;

    /// <summary>Back to the club already chosen, without changing it.</summary>
    [RelayCommand]
    private void CancelPicking()
    {
        if (SelectedClub is not null) IsPicking = false;
    }

    private TransferRequest? Request()
    {
        if (SelectedClub is null) return null;
        return new TransferRequest(Player, SelectedClub.Club)
        {
            Fee = Money.ToBase((double)(Fee ?? 0)),
            Date = IsNow || Date is null ? null : DateOnly.FromDateTime(Date.Value),
            Wage = CanEditWage && Wage is { } w ? Money.ToBase((double)w) : null,
            ContractExpiry = CanEditExpiry && ContractExpiry is { } e ? DateOnly.FromDateTime(e) : null,
            MoveMoney = MoveMoney,
        };
    }

    public string? Problem
    {
        get
        {
            if (!_session.Transfers.CanTransfer) return "Moving players isn't supported for this game version yet.";
            if (SelectedClub is null) return null;
            if (IsFuture && Date is null) return "Pick the date of the move.";
            return Request() is { } r ? _session.Transfers.Problem(r) : null;
        }
    }

    public bool HasProblem => Problem is not null;

    public string Summary
    {
        get
        {
            if (SelectedClub is null) return "";
            var fee = Fee is > 0 ? $"for {Values.FormatMoney(Money.ToBase((double)Fee))}" : "on a free";
            var when = IsNow || Date is null ? "now" : $"on {DateOnly.FromDateTime(Date.Value):dd MMM yyyy}";
            return $"{PlayerName} joins {SelectedClub.Name} {fee}, {when}.";
        }
    }

    private bool CanConfirm() => SelectedClub is not null && Problem is null;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        if (Request() is not { } request) return;
        try
        {
            var outcome = _session.Transfers.Transfer(request);
            var club = SelectedClub!.Name;
            _status(outcome switch
            {
                TransferOutcome.Scheduled => $"{PlayerName} will join {club} on {request.Date:dd MMM yyyy}. Keep the editor connected until then.",
                TransferOutcome.BookedInGame => $"{PlayerName}'s move to {club} is booked for {request.Date:dd MMM yyyy}. Save changes to apply in the game.",
                _ when _session.AutoSave => $"{PlayerName} joined {club}.",
                _ => $"{PlayerName} joins {club}. Save changes to apply in the game.",
            });
            CloseRequested?.Invoke();
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            _status(ex.Message);
        }
    }

    [RelayCommand]
    private void CancelScheduled()
    {
        _session.Transfers.Cancel(Player);
        RefreshScheduled();
        _status($"Cancelled {PlayerName}'s scheduled transfer.");
    }

    [RelayCommand] private void Close() => CloseRequested?.Invoke();

    private void RefreshScheduled()
    {
        ScheduledText = _session.Transfers.ScheduledFor(Player) is { } s
            ? $"Already set to join {_session.NameOf(EntityKind.Club, s.Request.ToClub.Id)} on {s.Date:dd MMM yyyy}. A new transfer replaces it."
            : null;
    }
}
