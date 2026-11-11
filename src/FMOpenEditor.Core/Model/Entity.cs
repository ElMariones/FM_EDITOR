namespace FMOpenEditor.Core.Model;

public enum EntityKind
{
    Player,
    Staff,
    Club,
    Competition,
    Nation,
    Stadium,
    City,
    Team,
    Continent,
    LocalRegion,
    Award,
    Injury,
    Language,
    MediaSource,
    Derby,
    StageName,
    Weather,
    Currency,
}

/// <summary>
/// A handle to one record in the game database. It carries no values itself: every value is
/// read through an <see cref="Data.IGameDatabase"/>, so the same handle works against live memory
/// and against the demo database.
/// </summary>
/// <param name="Address">Base address of the record in game memory, or 0 for non-memory sources.</param>
public sealed record Entity(EntityKind Kind, int Id, nint Address)
{
    public override string ToString() => $"{Kind} #{Id}";
}
