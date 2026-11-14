using FMOpenEditor.Core.Model;

namespace FMOpenEditor.Core.Editing;

/// <summary>A player's abilities, taken by <see cref="StatsCopy.Copy"/>: name, dates, contract and the rest are left out.</summary>
/// <param name="From">Who they were copied from, for messages.</param>
public sealed record PlayerStats(string From, IReadOnlyDictionary<string, object?> Values);

/// <summary>
/// Copies a player's abilities onto another player: current and potential ability, every attribute
/// (technical, mental, physical, goalkeeping, hidden), the positions and both feet. Nothing else.
/// </summary>
public static class StatsCopy
{
    private static readonly HashSet<string> Categories = ["Technical", "Mental", "Physical", "Goalkeeping", "Hidden", "Positions"];
    private static readonly HashSet<string> Extra = ["CurrentAbility", "PotentialAbility", "LeftFoot", "RightFoot"];

    /// <summary>Whether <paramref name="def"/> is one of the values copied.</summary>
    public static bool IsStat(PropertyDef def) =>
        !def.IsDerived && (Extra.Contains(def.Key) || (def.IsAttribute && Categories.Contains(def.Category)));

    public static PlayerStats Copy(EditorSession session, Entity player)
    {
        if (player.Kind != EntityKind.Player) throw new ArgumentException("Only players' stats can be copied.", nameof(player));
        var values = new Dictionary<string, object?>();
        foreach (var def in PropertyCatalog.For(EntityKind.Player))
            if (IsStat(def) && session.IsSupported(EntityKind.Player, def.Key) && session.TryRead(player, def.Key) is { } v)
                values[def.Key] = v;
        return new PlayerStats(session.NameOf(EntityKind.Player, player.Id), values);
    }

    /// <summary>
    /// Stages <paramref name="stats"/> on <paramref name="player"/> like any other edit. Abilities are
    /// written as copied, so automatic ability is paused meanwhile. Returns how many values changed.
    /// </summary>
    public static int Paste(EditorSession session, Entity player, PlayerStats stats)
    {
        if (player.Kind != EntityKind.Player) throw new ArgumentException("Stats can only be pasted onto a player.", nameof(player));
        var auto = session.AutoAbility;
        session.AutoAbility = false;
        var changed = 0;
        try
        {
            // Abilities last, so nothing written before them can move them.
            foreach (var (key, value) in stats.Values.OrderBy(kv => kv.Key is "CurrentAbility" or "PotentialAbility"))
            {
                if (!session.CanEdit(EntityKind.Player, key)) continue;
                if (Equals(session.TryRead(player, key), value)) continue;
                session.Set(player, key, value);
                changed++;
            }
        }
        finally { session.AutoAbility = auto; }
        return changed;
    }
}
