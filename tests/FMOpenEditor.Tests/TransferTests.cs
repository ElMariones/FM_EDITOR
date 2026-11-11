using FMOpenEditor.Core.Data;
using FMOpenEditor.Core.Definitions;
using FMOpenEditor.Core.Editing;

using FMOpenEditor.Core.Model;

namespace FMOpenEditor.Tests;

public class TransferTests
{
    /// <summary>The demo database without the game's future-transfer fields, and with a clock that can move.</summary>
    private sealed class NoFutureFields(IGameDatabase inner) : IGameDatabase
    {
        public DateOnly Date { get; set; } = inner.GameDate;
        public string DisplayName => inner.DisplayName;
        public bool IsLive => false;
        public DateOnly GameDate => Date;
        public IReadOnlyList<Entity> GetAll(EntityKind kind) => inner.GetAll(kind);
        public Entity? Find(EntityKind kind, int id) => inner.Find(kind, id);
        public bool Supports(EntityKind kind, string key) => !key.StartsWith("Transfer.", StringComparison.Ordinal) && inner.Supports(kind, key);
        public object? Read(Entity entity, string key) => inner.Read(entity, key);
        public void Write(Entity entity, string key, object? value) => inner.Write(entity, key, value);
        public void Refresh() => inner.Refresh();
        public void Dispose() => inner.Dispose();
    }

    private static (Entity Player, Entity From, Entity To) Pick(EditorSession s)
    {
        var player = s.GetAll(EntityKind.Player).First(p => s.Read(p, "Contract.Club") is int);
        var from = s.Database.Find(EntityKind.Club, (int)s.Read(player, "Contract.Club")!)!;
        var to = s.GetAll(EntityKind.Club).First(c => c.Id != from.Id);
        return (player, from, to);
    }

    private static long Money(EditorSession s, Entity e, string key) => (long)Values.ToDouble(s.Read(e, key)!);

    [Fact]
    public void Transfer_now_moves_the_player_sets_the_contract_and_pays_the_fee()
    {
        using var s = new EditorSession(new DemoDatabase());
        var (player, from, to) = Pick(s);
        s.Set(to, "Finance.TransferBudget", 50_000_000L);
        var buyerBalance = Money(s, to, "Finance.Balance");
        var sellerBalance = Money(s, from, "Finance.Balance");
        var expiry = s.GameDate.AddYears(4);

        var outcome = s.Transfers.Transfer(new TransferRequest(player, to)
        {
            Fee = 20_000_000, Wage = 75_000, ContractExpiry = expiry,
        });

        Assert.Equal(TransferOutcome.Done, outcome);
        Assert.Equal(to.Id, s.Read(player, "Contract.Club"));
        Assert.Equal(75_000L, s.Read(player, "Contract.Wage"));
        Assert.Equal(s.GameDate, s.Read(player, "Contract.Start"));
        if (s.CanEdit(EntityKind.Player, "DateJoined")) Assert.Equal(s.GameDate, s.Read(player, "DateJoined"));
        Assert.Equal(expiry, s.Read(player, "Contract.Expiry"));
        Assert.Equal(buyerBalance - 20_000_000, Money(s, to, "Finance.Balance"));
        Assert.Equal(30_000_000L, Money(s, to, "Finance.TransferBudget"));
        Assert.Equal(sellerBalance + 20_000_000, Money(s, from, "Finance.Balance"));
        // Staged like any other edit: nothing reaches the game until Save.
        Assert.Equal(from.Id, s.Database.Read(player, "Contract.Club"));
    }

    [Fact]
    public void Wage_left_empty_keeps_the_current_one_and_money_can_stay_put()
    {
        using var s = new EditorSession(new DemoDatabase());
        var (player, from, to) = Pick(s);
        var wage = s.Read(player, "Contract.Wage");
        var balance = Money(s, to, "Finance.Balance");

        s.Transfers.Transfer(new TransferRequest(player, to) { Fee = 1_000_000, MoveMoney = false });

        Assert.Equal(wage, s.Read(player, "Contract.Wage"));
        Assert.Equal(balance, Money(s, to, "Finance.Balance"));
    }

    [Fact]
    public void Problems_are_reported_before_anything_changes()
    {
        using var s = new EditorSession(new DemoDatabase());
        var (player, from, to) = Pick(s);
        Assert.NotNull(s.Transfers.Problem(new TransferRequest(player, from)));
        Assert.NotNull(s.Transfers.Problem(new TransferRequest(player, to) { ContractExpiry = s.GameDate.AddDays(-1) }));
        Assert.Throws<InvalidOperationException>(() => s.Transfers.Transfer(new TransferRequest(player, from)));
        Assert.Equal(0, s.Changes.Count);
    }

    [Fact]
    public void A_future_transfer_is_booked_in_the_game_when_it_has_the_fields()
    {
        using var s = new EditorSession(new DemoDatabase());
        var (player, from, to) = Pick(s);
        var date = s.GameDate.AddMonths(2);

        var outcome = s.Transfers.Transfer(new TransferRequest(player, to) { Date = date, Fee = 5_000_000 });

        Assert.Equal(TransferOutcome.BookedInGame, outcome);
        Assert.Equal(to.Id, s.Read(player, "Transfer.FutureClub"));
        Assert.Equal(date, s.Read(player, "Transfer.FutureDate"));
        Assert.Equal(from.Id, s.Read(player, "Contract.Club"));
    }

    [Fact]
    public void Otherwise_the_editor_makes_the_move_when_the_date_comes()
    {
        var db = new NoFutureFields(new DemoDatabase());
        using var s = new EditorSession(db);
        var (player, from, to) = Pick(s);
        var date = s.GameDate.AddDays(10);

        Assert.Equal(TransferOutcome.Scheduled, s.Transfers.Transfer(new TransferRequest(player, to) { Date = date, Wage = 9_000 }));
        Assert.NotNull(s.Transfers.ScheduledFor(player));

        Assert.Equal(0, s.Transfers.Tick());
        Assert.Equal(from.Id, db.Read(player, "Contract.Club"));

        db.Date = date;
        Assert.Equal(1, s.Transfers.Tick());
        Assert.Equal(to.Id, db.Read(player, "Contract.Club"));
        Assert.Equal(9_000L, db.Read(player, "Contract.Wage"));
        Assert.Equal(date, db.Read(player, "Contract.Start"));
        Assert.Empty(s.Transfers.Scheduled);
        Assert.Equal(0, s.Changes.Count);
    }

    [Fact]
    public void A_scheduled_transfer_is_dropped_if_the_game_moved_the_player_first()
    {
        var db = new NoFutureFields(new DemoDatabase());
        using var s = new EditorSession(db);
        var (player, from, to) = Pick(s);
        var elsewhere = s.GetAll(EntityKind.Club).First(c => c.Id != from.Id && c.Id != to.Id);
        var balance = Money(s, to, "Finance.Balance");
        s.Transfers.Transfer(new TransferRequest(player, to) { Date = s.GameDate.AddDays(5), Fee = 1_000_000 });

        db.Write(player, "Contract.Club", elsewhere.Id);   // the game sells him meanwhile
        db.Date = s.GameDate.AddDays(5);

        Assert.Equal(0, s.Transfers.Tick());
        Assert.Equal(elsewhere.Id, db.Read(player, "Contract.Club"));
        Assert.Equal(balance, Money(s, to, "Finance.Balance"));
        Assert.Single(s.Transfers.TakeDropped());
        Assert.Empty(s.Transfers.Scheduled);
    }

    [Fact]
    public void Scheduled_transfers_can_be_cancelled()
    {
        using var s = new EditorSession(new NoFutureFields(new DemoDatabase()));
        var (player, _, to) = Pick(s);
        s.Transfers.Transfer(new TransferRequest(player, to) { Date = s.GameDate.AddDays(3) });
        s.Transfers.Cancel(player);
        Assert.Empty(s.Transfers.Scheduled);
    }
}
