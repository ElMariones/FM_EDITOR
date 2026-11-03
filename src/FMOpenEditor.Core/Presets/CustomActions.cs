using FMOpenEditor.Core.Editing;
using FMOpenEditor.Core.Model;

namespace FMOpenEditor.Core.Presets;

/// <summary>A preset step that needs logic beyond setting properties, usually because it touches a related entity.</summary>
public interface ICustomAction
{
    string Name { get; }
    string Description { get; }

    /// <summary>Returns the number of values changed.</summary>
    int Apply(EditorSession session, Entity entity, string? parameter);
}

public static class CustomActions
{
    private static readonly Dictionary<string, ICustomAction> Registry =
        new ICustomAction[] { new ImproveTeamCohesion(), new ReleaseOnFree() }
            .ToDictionary(a => a.Name, StringComparer.OrdinalIgnoreCase);

    public static IEnumerable<ICustomAction> All => Registry.Values;
    public static ICustomAction? Find(string name) => Registry.GetValueOrDefault(name);
    public static void Register(ICustomAction action) => Registry[action.Name] = action;

    private static bool TrySet(EditorSession s, Entity e, string key, object? value) =>
        s.CanEdit(e.Kind, key) && PresetEngine.ApplyOp(s, e, key, PresetOp.Set, value);

    /// <summary>Sets the player's club's team cohesion to the maximum.</summary>
    private sealed class ImproveTeamCohesion : ICustomAction
    {
        public string Name => "ImproveTeamCohesion";
        public string Description => "Maximise the team cohesion of the player's club.";

        public int Apply(EditorSession s, Entity e, string? parameter)
        {
            var club = e.Kind == EntityKind.Club ? e
                : s.TryRead(e, "Contract.Club") is int id ? s.Database.Find(EntityKind.Club, id) : null;
            return club is not null && TrySet(s, club, "TeamCohesion", 100) ? 1 : 0;
        }
    }

    /// <summary>Cancels the contract: the player or staff member becomes a free agent.</summary>
    private sealed class ReleaseOnFree : ICustomAction
    {
        public string Name => "ReleaseOnFree";
        public string Description => "Terminate the contract and make them a free agent.";

        public int Apply(EditorSession s, Entity e, string? parameter)
        {
            if (!s.CanEdit(e.Kind, "Contract.Club")) return 0;
            var n = 0;
            if (TrySet(s, e, "Contract.Club", null)) n++;
            if (TrySet(s, e, "Contract.Wage", 0)) n++;
            if (TrySet(s, e, "Contract.Expiry", null)) n++;
            if (TrySet(s, e, "Contract.Start", null)) n++;
            if (TrySet(s, e, "Contract.SquadStatus", 0)) n++;
            if (TrySet(s, e, "Contract.ReleaseClause", 0)) n++;
            return n;
        }
    }
}
