using FMOpenEditor.Core.Model;

namespace FMOpenEditor.Core.Data;

/// <summary>
/// The game's data, as seen by the editor. Values are typed per <see cref="ValueKind"/>
/// (see <see cref="Values.Coerce"/>). Derived properties are not handled here; see
/// <see cref="Editing.EditorSession"/>.
/// </summary>
public interface IGameDatabase : IDisposable
{
    string DisplayName { get; }

    /// <summary>True when values come from (and go to) a running game.</summary>
    bool IsLive { get; }

    /// <summary>The in-game date, used for ages and contract filters.</summary>
    DateOnly GameDate { get; }

    IReadOnlyList<Entity> GetAll(EntityKind kind);
    Entity? Find(EntityKind kind, int id);

    /// <summary>The managers the people playing this save control (none when the game version doesn't say).</summary>
    IReadOnlyList<Entity> HumanManagers => [];

    /// <summary>The currency id the game itself shows amounts in, when known.</summary>
    int? GameCurrencyId => null;

    /// <summary>Whether this source can read and write <paramref name="key"/> for <paramref name="kind"/>.</summary>
    bool Supports(EntityKind kind, string key);

    /// <summary>Whether <paramref name="key"/> may be written, not just read (some links are display-only).</summary>
    bool CanWrite(EntityKind kind, string key) => Supports(kind, key);

    object? Read(Entity entity, string key);
    void Write(Entity entity, string key, object? value);

    /// <summary>Re-reads entity lists (players move, regens are created while the game runs).</summary>
    void Refresh();
}
