using FMOpenEditor.Core.Model;

namespace FMOpenEditor.Core.Definitions;

/// <summary>Loads every definition in a folder and picks the one that matches the running game.</summary>
public sealed class DefinitionLibrary
{
    public IReadOnlyList<GameDefinition> Definitions { get; }
    public IReadOnlyList<string> Errors { get; }

    private DefinitionLibrary(List<GameDefinition> defs, List<string> errors)
    {
        Definitions = defs;
        Errors = errors;
    }

    public static DefinitionLibrary LoadFolder(string folder)
    {
        var defs = new List<GameDefinition>();
        var errors = new List<string>();
        if (Directory.Exists(folder))
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*.json", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(file).StartsWith('_')) continue; // templates
                try { defs.Add(GameDefinition.Load(file)); }
                catch (Exception ex) { errors.Add($"{Path.GetFileName(file)}: {ex.Message}"); }
            }
        }
        return new DefinitionLibrary(defs, errors);
    }

    /// <summary>Exact module version match first, then the newest definition for the same major version.</summary>
    public GameDefinition? Match(string moduleVersion)
    {
        var exact = Definitions.FirstOrDefault(d => d.ModuleVersions.Contains(moduleVersion, StringComparer.OrdinalIgnoreCase));
        if (exact is not null) return exact;
        var major = moduleVersion.Split('.')[0];
        return Definitions.Where(d => d.Version.Split('.')[0] == major)
                          .OrderByDescending(d => d.Version, StringComparer.Ordinal)
                          .FirstOrDefault();
    }

    /// <summary>A definition with every catalog property listed and unmapped, for contributors to fill in.</summary>
    public static GameDefinition CreateTemplate(string game, string version)
    {
        var def = new GameDefinition
        {
            Game = game,
            Version = version,
            Notes = "Template. Fill in roots, collections and field offsets; see docs/DEFINITIONS.md.",
            Roots = { ["People"] = new RootDef { Description = "Global list of every person (players + staff)." },
                      ["Clubs"] = new RootDef { Description = "Global list of clubs." },
                      ["Competitions"] = new RootDef { Description = "Global list of competitions." },
                      ["Nations"] = new RootDef { Description = "Global list of nations." } },
        };
        foreach (var kind in Enum.GetValues<EntityKind>())
        {
            def.Collections[kind.ToString()] = new CollectionDef
            {
                Root = kind switch
                {
                    EntityKind.Player or EntityKind.Staff => "People",
                    EntityKind.Club => "Clubs",
                    EntityKind.Competition => "Competitions",
                    _ => "Nations",
                },
            };
            var fields = new Dictionary<string, FieldDef> { ["Id"] = new() { Type = FieldType.I32 } };
            foreach (var p in PropertyCatalog.For(kind).Where(p => !p.IsDerived))
            {
                fields[p.Key] = new FieldDef { Type = DefaultFieldType(p), Scale = p.IsAttribute ? 5 : 1 };
            }
            def.Structs[kind.ToString()] = fields;
        }
        return def;
    }

    private static FieldType DefaultFieldType(PropertyDef p) => p.Kind switch
    {
        ValueKind.Text => FieldType.Utf16Ptr,
        ValueKind.Money => FieldType.I64,
        ValueKind.Boolean => FieldType.Bool,
        ValueKind.Date => FieldType.FmDate,
        ValueKind.Reference => FieldType.EntityPtr,
        ValueKind.Decimal => FieldType.F32,
        _ when p.IsAttribute => FieldType.U8,
        _ when p.Max <= 255 && p.Min >= 0 => FieldType.U8,
        _ when p.Max <= short.MaxValue && p.Min >= short.MinValue => FieldType.I16,
        _ => FieldType.I32,
    };
}
