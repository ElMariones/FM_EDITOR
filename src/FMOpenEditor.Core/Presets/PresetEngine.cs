using FMOpenEditor.Core.Editing;
using FMOpenEditor.Core.Model;
using FMOpenEditor.Core.Search;

namespace FMOpenEditor.Core.Presets;

public sealed record PresetResult(int Entities, int Changes, IReadOnlyList<string> Skipped);

/// <summary>Applies presets and mass edits through an <see cref="EditorSession"/>, so they're staged like any edit.</summary>
public static class PresetEngine
{
    public static PresetResult Apply(EditorSession session, Preset preset, IEnumerable<Entity> selection)
    {
        var targets = preset.Scope == PresetScope.All
            ? session.GetAll(preset.Target)
            : selection.Where(e => e.Kind == preset.Target);

        var skipped = new SortedSet<string>();
        int entities = 0, changes = 0;

        foreach (var entity in targets.ToList())
        {
            if (!preset.Where.All(f => session.IsSupported(entity.Kind, f.Property)
                                       && SearchEngine.Matches(PropertyCatalog.Get(entity.Kind, f.Property),
                                           session.TryRead(entity, f.Property), f)))
                continue;

            entities++;
            foreach (var action in preset.Actions)
            {
                if (action.Custom is { } custom)
                {
                    if (CustomActions.Find(custom) is { } impl) changes += impl.Apply(session, entity, action.Parameter);
                    else skipped.Add($"unknown action '{custom}'");
                    continue;
                }
                foreach (var key in ResolveKeys(entity.Kind, action))
                {
                    if (!session.CanEdit(entity.Kind, key))
                    {
                        skipped.Add(PropertyCatalog.Find(entity.Kind, key)?.Label ?? key);
                        continue;
                    }
                    var value = action.FromProperty is { } from ? session.TryRead(entity, from) : action.Value;
                    if (ApplyOp(session, entity, key, action.Op, value)) changes++;
                }
            }
        }
        return new PresetResult(entities, changes, skipped.ToList());
    }

    /// <summary>Applies the same change to every entity: the heart of Mass Edit.</summary>
    public static PresetResult MassEdit(EditorSession session, IEnumerable<Entity> entities, string key, PresetOp op, object? value)
    {
        var preset = new Preset
        {
            Name = "Mass edit",
            Target = entities.FirstOrDefault()?.Kind ?? EntityKind.Player,
            Actions = [new PresetAction { Property = key, Op = op, Value = value }],
        };
        return Apply(session, preset, entities);
    }

    public static IEnumerable<string> ResolveKeys(EntityKind kind, PresetAction action)
    {
        if (action.Property is not { } prop) return [];
        if (!prop.EndsWith('*')) return [prop];
        var prefix = prop[..^1];
        return PropertyCatalog.For(kind)
            .Where(p => !p.IsDerived && p.Key.StartsWith(prefix, StringComparison.Ordinal))
            .Where(p => action.Category is null || string.Equals(p.Category, action.Category, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Key);
    }

    /// <summary>Returns true if the value changed.</summary>
    public static bool ApplyOp(EditorSession session, Entity entity, string key, PresetOp op, object? value)
    {
        var def = PropertyCatalog.Get(entity.Kind, key);
        var current = session.TryRead(entity, key);
        var operand = Values.Coerce(def with { Min = double.MinValue, Max = double.MaxValue }, value);

        object? next = op switch
        {
            PresetOp.Set => operand,
            PresetOp.Add when current is not null && operand is not null =>
                Values.ToDouble(current) + Values.ToDouble(operand),
            PresetOp.Multiply when current is not null && operand is not null =>
                Values.ToDouble(current) * Values.ToDouble(operand),
            PresetOp.AtLeast => Values.Compare(current, operand) < 0 ? operand : current,
            PresetOp.AtMost => Values.Compare(current, operand) > 0 ? operand : current,
            _ => current,
        };
        next = Values.Coerce(def, next);
        if (Values.AreEqual(current, next)) return false;
        session.Set(entity, key, next);
        return true;
    }
}
