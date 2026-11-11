using FMOpenEditor.Core.Model;

namespace FMOpenEditor.Core.Editing;

/// <summary>
/// Keeps a player's current ability in step with their attributes, and their potential ability at or
/// above it. Raising every attribute that counts to 20 takes current ability to 200, dropping them all
/// to 1 takes it to 1, and in between it moves in proportion from where the player started.
/// </summary>
/// <remarks>
/// The game works ability out from position-weighted attributes; this is a simpler rule that stays
/// predictable: the attributes that count are the technical (or goalkeeping, for a keeper), mental and
/// physical ones, and their average maps onto current ability along two straight lines that meet at
/// the player's starting point: (1, 1) to (start average, start ability) to (20, 200). Keeping the
/// starting point means edits can be undone exactly by putting the attributes back.
/// </remarks>
internal sealed class AbilityBalancer(EditorSession session)
{
    private const string Current = "CurrentAbility";
    private const string Potential = "PotentialAbility";

    private sealed record Anchor(bool Keeper, double Average, int Ability);

    private readonly Dictionary<Entity, Anchor> _anchors = [];

    /// <summary>Forgets every starting point, e.g. after the values they were taken from are discarded.</summary>
    public void Reset() => _anchors.Clear();

    /// <summary>The user set current ability themselves: measure from there from now on.</summary>
    public void Forget(Entity entity) => _anchors.Remove(entity);

    /// <summary>
    /// Called after <paramref name="key"/> has been set; <paramref name="before"/> is the value it had.
    /// Returns the (key, value) pairs to write, in order.
    /// </summary>
    public IEnumerable<(string Key, int Value)> OnAttributeSet(Entity entity, string key, object? before)
    {
        if (entity.Kind != EntityKind.Player) yield break;
        if (!session.IsSupported(entity.Kind, Current)) yield break;

        var keeper = IsKeeper(entity);
        var keys = CountingKeys(entity, keeper);
        if (!keys.Contains(key)) yield break;

        if (!_anchors.TryGetValue(entity, out var anchor) || anchor.Keeper != keeper)
        {
            if (session.TryRead(entity, Current) is not { } ca || before is null) yield break;
            var avgBefore = Average(entity, keys, (key, Values.ToDouble(before)));
            if (avgBefore is null) yield break;
            anchor = new Anchor(keeper, avgBefore.Value, (int)Values.ToDouble(ca));
            _anchors[entity] = anchor;
        }

        if (Average(entity, keys, null) is not { } avg) yield break;
        var target = (int)Math.Round(Math.Clamp(Map(anchor, avg), 1, 200));

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

    private static double Map(Anchor a, double avg)
    {
        if (avg >= a.Average)
            return a.Average >= 20 ? a.Ability : a.Ability + (avg - a.Average) * (200 - a.Ability) / (20 - a.Average);
        return a.Average <= 1 ? a.Ability : a.Ability - (a.Average - avg) * (a.Ability - 1) / (a.Average - 1);
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
