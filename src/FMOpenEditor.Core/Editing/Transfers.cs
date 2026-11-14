using FMOpenEditor.Core.Model;

namespace FMOpenEditor.Core.Editing;

/// <summary>What the user asked for: who moves where, for how much, and when.</summary>
public sealed record TransferRequest(Entity Player, Entity ToClub)
{
    /// <summary>Fee in pounds (the game's base currency). 0 for a free transfer.</summary>
    public long Fee { get; init; }

    /// <summary>When the move happens; null (or a date not after the game date) means straight away.</summary>
    public DateOnly? Date { get; init; }

    /// <summary>Weekly wage at the new club, in pounds; null keeps the current wage.</summary>
    public long? Wage { get; init; }

    /// <summary>When the new contract ends; null keeps the current expiry.</summary>
    public DateOnly? ContractExpiry { get; init; }

    /// <summary>Take the fee from the buyer's balance and transfer budget and give it to the seller.</summary>
    public bool MoveMoney { get; init; } = true;
}

/// <summary>A future transfer the editor carries out itself when the game reaches its date.</summary>
/// <param name="FromClubId">The player's club when it was scheduled; null for a free agent.</param>
public sealed record ScheduledTransfer(TransferRequest Request, DateOnly Date, int? FromClubId);

public enum TransferOutcome
{
    /// <summary>Staged like any other edit (or written straight away with auto-save).</summary>
    Done,
    /// <summary>Booked in the game's own future-transfer fields.</summary>
    BookedInGame,
    /// <summary>Held by the editor until the game reaches the date.</summary>
    Scheduled,
}

/// <summary>
/// Moves players between clubs. A transfer is a set of ordinary edits (club, wage, contract dates,
/// both clubs' money), so it is staged, saved and undone like everything else. A future transfer is
/// booked in the game's own fields when the version has them; otherwise the editor keeps it and makes
/// the move when the game date gets there, for as long as it stays connected.
/// </summary>
public sealed class TransferService(EditorSession session)
{
    private const string Club = "Contract.Club";
    private readonly List<ScheduledTransfer> _scheduled = [];

    public IReadOnlyList<ScheduledTransfer> Scheduled => _scheduled;

    /// <summary>Raised when the scheduled list changes or a scheduled transfer goes through.</summary>
    public event Action? Changed;

    /// <summary>
    /// Scheduled transfers dropped because the player had already moved by the time their date came
    /// (the game sold, released or loaned them). Cleared by <see cref="TakeDropped"/>.
    /// </summary>
    private readonly List<ScheduledTransfer> _dropped = [];

    /// <summary>Returns, and forgets, the scheduled transfers dropped since the last call.</summary>
    public IReadOnlyList<ScheduledTransfer> TakeDropped()
    {
        var list = _dropped.ToList();
        _dropped.Clear();
        return list;
    }

    /// <summary>Whether players can be moved at all in this game version.</summary>
    public bool CanTransfer => session.CanEdit(EntityKind.Player, Club);

    /// <summary>Whether the game's own future-transfer fields are available.</summary>
    public bool CanBookInGame =>
        session.CanEdit(EntityKind.Player, "Transfer.FutureClub") && session.CanEdit(EntityKind.Player, "Transfer.FutureDate");

    /// <summary>Why <paramref name="request"/> can't go ahead, or null when it can.</summary>
    public string? Problem(TransferRequest request)
    {
        if (!CanTransfer) return "Moving players isn't supported for this game version yet.";
        if (request.Player.Kind != EntityKind.Player) return "Only players can be transferred.";
        if (request.ToClub.Kind != EntityKind.Club) return "Pick a club to move to.";
        if (session.TryRead(request.Player, Club) is int current && current == request.ToClub.Id)
            return "They already play for that club.";
        if (session.TryRead(request.Player, "Loan.Club") is int)
            return "Players out on loan can't be transferred yet. Wait until the loan ends.";
        if (session.Database.WriteProblem(request.Player, Club, request.ToClub.Id) is { } why) return why;
        if (request.Fee < 0) return "The fee can't be negative.";
        if (request.Wage < 0) return "The wage can't be negative.";
        var start = StartDate(request);
        if (request.ContractExpiry is { } end && end <= start) return "The contract has to end after the transfer date.";
        return null;
    }

    public TransferOutcome Transfer(TransferRequest request)
    {
        if (Problem(request) is { } problem) throw new InvalidOperationException(problem);

        if (request.Date is { } date && date > session.GameDate)
        {
            if (CanBookInGame)
            {
                session.Set(request.Player, "Transfer.FutureClub", request.ToClub.Id);
                session.Set(request.Player, "Transfer.FutureDate", date);
                if (session.CanEdit(EntityKind.Player, "Transfer.FutureFee")) session.Set(request.Player, "Transfer.FutureFee", request.Fee);
                return TransferOutcome.BookedInGame;
            }
            _scheduled.RemoveAll(s => s.Request.Player == request.Player);
            _scheduled.Add(new ScheduledTransfer(request, date, session.TryRead(request.Player, Club) as int?));
            Changed?.Invoke();
            return TransferOutcome.Scheduled;
        }

        Apply(request, session.Set);
        return TransferOutcome.Done;
    }

    public void Cancel(Entity player)
    {
        if (_scheduled.RemoveAll(s => s.Request.Player == player) > 0) Changed?.Invoke();
    }

    public ScheduledTransfer? ScheduledFor(Entity player) => _scheduled.FirstOrDefault(s => s.Request.Player == player);

    /// <summary>
    /// Carries out every scheduled transfer whose date has come. These go straight into the game
    /// rather than waiting in the pending edits, since nobody is there to press Save at that moment.
    /// If the player is no longer at the club they were at when it was scheduled, the game (or the
    /// user) has moved them meanwhile: that move wins and the scheduled one is dropped, see
    /// <see cref="TakeDropped"/>. Returns how many went through.
    /// </summary>
    public int Tick()
    {
        var due = _scheduled.Where(s => s.Date <= session.GameDate).ToList();
        if (due.Count == 0) return 0;
        var done = 0;
        foreach (var s in due)
        {
            _scheduled.Remove(s);
            if (session.TryRead(s.Request.Player, Club) as int? != s.FromClubId
                || Problem(s.Request with { Date = null }) is not null)
            {
                _dropped.Add(s);
                continue;
            }
            try
            {
                Apply(s.Request, (e, k, v) =>
                {
                    var def = PropertyCatalog.Get(e.Kind, k);
                    session.Database.Write(e, k, Values.Coerce(def, v));
                });
                done++;
            }
            catch (Exception) { /* the player may have retired or moved; drop it */ }
        }
        session.RaiseValuesChanged();
        Changed?.Invoke();
        return done;
    }

    private DateOnly StartDate(TransferRequest r) =>
        r.Date is { } d && d > session.GameDate ? d : session.GameDate;

    private void Apply(TransferRequest r, Action<Entity, string, object?> write)
    {
        var player = r.Player;
        var from = session.TryRead(player, Club) is int fromId ? session.Database.Find(EntityKind.Club, fromId) : null;
        var start = StartDate(r);

        // A captain of the club he leaves gives the armband up; the club picks a new one, as the game does.
        if (from is not null)
            foreach (var role in new[] { "Captain", "ViceCaptain" })
                if (Can(from, role) && session.TryRead(from, role) is int id && id == player.Id)
                    write(from, role, null);

        write(player, Club, r.ToClub.Id);
        if (r.Wage is { } wage && Can(player, "Contract.Wage")) write(player, "Contract.Wage", wage);
        if (Can(player, "Contract.Start")) write(player, "Contract.Start", start);
        if (Can(player, "Contract.Joined")) write(player, "Contract.Joined", start);
        if (Can(player, "DateJoined")) write(player, "DateJoined", start);
        if (from is not null && Can(player, "LastClub")) write(player, "LastClub", from.Id);
        // His number belonged to the old club, and he is no longer for sale or loan.
        if (Can(player, "Contract.SquadNumber")) write(player, "Contract.SquadNumber", 255);
        if (Can(player, "Contract.TransferListed")) write(player, "Contract.TransferListed", false);
        if (Can(player, "Contract.LoanListed")) write(player, "Contract.LoanListed", false);
        if (r.ContractExpiry is { } end && Can(player, "Contract.Expiry")) write(player, "Contract.Expiry", end);
        if (Can(player, "Contract.ReleaseClause")) write(player, "Contract.ReleaseClause", 0L);

        if (!r.MoveMoney || r.Fee <= 0) return;
        AddMoney(r.ToClub, "Finance.Balance", -r.Fee, write);
        AddMoney(r.ToClub, "Finance.TransferBudget", -r.Fee, write, floor: 0);
        if (from is not null) AddMoney(from, "Finance.Balance", r.Fee, write);
    }

    private bool Can(Entity e, string key) => session.CanEdit(e.Kind, key);

    private void AddMoney(Entity club, string key, long amount, Action<Entity, string, object?> write, long? floor = null)
    {
        if (!Can(club, key) || session.TryRead(club, key) is not { } now) return;
        var next = (long)Values.ToDouble(now) + amount;
        if (floor is { } f) next = Math.Max(f, next);
        write(club, key, next);
    }
}
