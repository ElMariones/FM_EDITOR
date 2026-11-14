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

    /// <summary>Whether this source can read and write <paramref name="key"/> for <paramref name="kind"/>.</summary>
    bool Supports(EntityKind kind, string key);

    /// <summary>Whether <paramref name="key"/> may be written, not just read (some links are display-only).</summary>
    bool CanWrite(EntityKind kind, string key) => Supports(kind, key);

    /// <summary>
    /// Why writing <paramref name="value"/> to <paramref name="key"/> can't work right now, checked cheaply
    /// before anything is staged; null when nothing stands in the way that can be known in advance.
    /// </summary>
    string? WriteProblem(Entity entity, string key, object? value) => null;

    /// <summary>The game's per-position ability weights (see <see cref="Definitions.GameDefinition.AbilityWeights"/>), or null.</summary>
    IReadOnlyDictionary<string, Definitions.AbilityWeightsDef>? AbilityWeights => null;

    object? Read(Entity entity, string key);
    void Write(Entity entity, string key, object? value);

    /// <summary>Re-reads entity lists (players move, regens are created while the game runs).</summary>
    void Refresh();
}
