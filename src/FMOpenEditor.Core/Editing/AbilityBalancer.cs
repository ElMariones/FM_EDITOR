using FMOpenEditor.Core.Definitions;
using FMOpenEditor.Core.Model;

namespace FMOpenEditor.Core.Editing;

/// <summary>
/// Keeps a player's current ability in step with their attributes, and their potential ability at or
/// above it, so the game doesn't take the attributes back down to fit an ability that's too low.
/// </summary>
/// <remarks>
/// <para>
/// With the game's weights (<see cref="GameDefinition.AbilityWeights"/>), ability is priced as the game
/// does: each position has its own weight per attribute (pace and acceleration cost a lot, corners
/// little, hidden attributes nothing; both feet count), and a player is priced at the dearest of his
/// natural positions. The editor measures the change from where the player started, so the game's own
/// rounding is kept: target = starting ability + price now − price at the start. Putting the attributes
/// back therefore restores the starting ability exactly.
/// </para>
/// <para>
/// Without weights it falls back to the plain average of the technical (or goalkeeping), mental and
/// physical attributes, mapped onto ability along (1, 1) → (start average, start ability) → (20, 200).
/// </para>
/// </remarks>
internal sealed class AbilityBalancer(EditorSession session)
{
    private const string Current = "CurrentAbility";
    private const string Potential = "PotentialAbility";

    /// <summary>Where the player started: their ability and, depending on the mode, its price or average.</summary>
    private sealed record Anchor(bool Weighted, bool Keeper, double Measure, int Ability);

    private readonly Dictionary<Entity, Anchor> _anchors = [];

    private IReadOnlyDictionary<string, AbilityWeightsDef>? Weights => session.Database.AbilityWeights;

    /// <summary>Forgets every starting point, e.g. after the values they were taken from are discarded.</summary>
    public void Reset() => _anchors.Clear();

    /// <summary>The user set current ability themselves: measure from there from now on.</summary>
    public void Forget(Entity entity) => _anchors.Remove(entity);

    /// <summary>Whether setting <paramref name="key"/> can change <paramref name="entity"/>'s ability.</summary>
    public bool Watches(Entity entity, string key)
    {
        if (entity.Kind != EntityKind.Player) return false;
        if (Weights is { } w) return w.ContainsKey(key) || w.Values.Any(p => p.Weights.ContainsKey(key));
        return PropertyCatalog.Get(entity.Kind, key).IsAttribute;
    }

    /// <summary>
    /// Called after <paramref name="key"/> has been set; <paramref name="before"/> is the value it had.
    /// Returns the (key, value) pairs to write, in order.
    /// </summary>
    public IEnumerable<(string Key, int Value)> OnAttributeSet(Entity entity, string key, object? before)
    {
        if (!Watches(entity, key) || !session.IsSupported(entity.Kind, Current)) yield break;

        int target;
        if (Weights is { } weights)
        {
            if (!_anchors.TryGetValue(entity, out var anchor) || !anchor.Weighted)
            {
                if (session.TryRead(entity, Current) is not { } ca || before is null) yield break;
                if (Price(entity, weights, (key, Values.ToDouble(before))) is not { } start) yield break;
                anchor = new Anchor(true, false, start, (int)Values.ToDouble(ca));
                _anchors[entity] = anchor;
            }
            if (Price(entity, weights, null) is not { } now) yield break;
            target = (int)Math.Round(Math.Clamp(anchor.Ability + now - anchor.Measure, 1, 200));
        }
        else
        {
            var keeper = IsKeeper(entity);
            var keys = CountingKeys(entity, keeper);
            if (!keys.Contains(key)) yield break;
            if (!_anchors.TryGetValue(entity, out var anchor) || anchor.Weighted || anchor.Keeper != keeper)
            {
                if (session.TryRead(entity, Current) is not { } ca || before is null) yield break;
                if (Average(entity, keys, (key, Values.ToDouble(before))) is not { } avgBefore) yield break;
                anchor = new Anchor(false, keeper, avgBefore, (int)Values.ToDouble(ca));
                _anchors[entity] = anchor;
            }
            if (Average(entity, keys, null) is not { } avg) yield break;
            target = (int)Math.Round(Math.Clamp(Map(anchor, avg), 1, 200));
        }

        if (session.TryRead(entity, Current) is { } current && (int)Values.ToDouble(current) != target)
            yield return (Current, target);

        // Potential is never below current. A negative potential is a range the game rolls when a new
        // save starts, not a number to compare with, so it's left alone.
        if (session.IsSupported(entity.Kind, Potential)
            && session.TryRead(entity, Potential) is { } pa
            && Values.ToDouble(pa) is var potential and >= 0
            && potential < target)
        {
            yield return (Potential, target);
        }
    }

    /// <summary>
    /// What the game would price the player at: the dearest of his natural positions (rated 20, or his
    /// best-rated ones if none is), each priced as its constant plus weight × value per attribute.
    /// </summary>
    private double? Price(Entity entity, IReadOnlyDictionary<string, AbilityWeightsDef> weights, (string Key, double Value)? replace)
    {
        double? Value(string k)
        {
            if (replace is { } r && r.Key == k) return r.Value;
            return session.TryRead(entity, k) is { } raw ? Values.ToDouble(raw) : null;
        }

        var ratings = new List<(AbilityWeightsDef Def, double Rating)>();
        foreach (var (pos, def) in weights)
            if (Value(pos) is { } rating) ratings.Add((def, rating));
        if (ratings.Count == 0) return null;
        var best = Math.Min(20, ratings.Max(r => r.Rating));

        double? price = null;
        foreach (var (def, rating) in ratings)
        {
            if (rating < best) continue;
            var sum = def.Const;
            foreach (var (k, w) in def.Weights)
            {
                if (Value(k) is not { } v) return null;
                sum += w * v;
            }
            price = price is { } p ? Math.Max(p, sum) : sum;
        }
        return price;
    }

    private static double Map(Anchor a, double avg)
    {
        if (avg >= a.Measure)
            return a.Measure >= 20 ? a.Ability : a.Ability + (avg - a.Measure) * (200 - a.Ability) / (20 - a.Measure);
        return a.Measure <= 1 ? a.Ability : a.Ability - (a.Measure - avg) * (a.Ability - 1) / (a.Measure - 1);
    }

    private bool IsKeeper(Entity entity) =>
        session.IsSupported(entity.Kind, "Pos.GK")
        && session.TryRead(entity, "Pos.GK") is { } gk
        && Values.ToDouble(gk) >= 15;

    private HashSet<string> CountingKeys(Entity entity, bool keeper)
    {
        var categories = keeper
            ? new[] { "Goalkeeping", "Mental", "Physical" }
            : new[] { "Technical", "Mental", "Physical" };
        return PropertyCatalog.For(entity.Kind)
            .Where(p => p.IsAttribute && p.Key.StartsWith("Attr.", StringComparison.Ordinal) && categories.Contains(p.Category))
            .Where(p => session.IsSupported(entity.Kind, p.Key))
            .Select(p => p.Key)
            .ToHashSet();
    }

    private double? Average(Entity entity, HashSet<string> keys, (string Key, double Value)? replace)
    {
        double sum = 0;
        var n = 0;
        foreach (var k in keys)
        {
            double v;
            if (replace is { } r && r.Key == k) v = r.Value;
            else if (session.TryRead(entity, k) is { } raw) v = Values.ToDouble(raw);
            else continue;
            sum += v;
            n++;
        }
        return n == 0 ? null : sum / n;
    }
}
