using FMOpenEditor.Core.Data;
using FMOpenEditor.Core.Definitions;
using FMOpenEditor.Core.Editing;
using FMOpenEditor.Core.Model;
using FMOpenEditor.Core.Presets;
using FMOpenEditor.Core.Search;

namespace FMOpenEditor.Tests;

public class EditingTests
{
    private static EditorSession NewSession() => new(new DemoDatabase());

    private static string RepoFolder(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, name))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException(name), name);
    }

    [Fact]
    public void Edits_are_staged_until_saved_and_can_be_undone()
    {
        using var s = NewSession();
        var p = s.GetAll(EntityKind.Player)[0];
        var before = s.Read(p, "Attr.Finishing");

        s.Set(p, "Attr.Finishing", 20);
        Assert.Equal(20, s.Read(p, "Attr.Finishing"));
        Assert.Equal(before, s.Database.Read(p, "Attr.Finishing"));
        Assert.Equal(1, s.Changes.Count);

        Assert.Empty(s.Save());
        Assert.Equal(20, s.Database.Read(p, "Attr.Finishing"));
        Assert.Equal(0, s.Changes.Count);

        s.UndoLastSave();
        Assert.Equal(before, s.Database.Read(p, "Attr.Finishing"));
    }

    [Fact]
    public void Setting_back_to_the_original_clears_the_pending_change()
    {
        using var s = NewSession();
        var p = s.GetAll(EntityKind.Player)[0];
        var before = s.Read(p, "Morale");
        s.Set(p, "Morale", 20);
        s.Set(p, "Morale", before);
        Assert.Equal(0, s.Changes.Count);
    }

    [Fact]
    public void Values_are_clamped_to_the_property_range()
    {
        using var s = NewSession();
        var p = s.GetAll(EntityKind.Player)[0];
        s.Set(p, "Attr.Pace", 99);
        Assert.Equal(20, s.Read(p, "Attr.Pace"));
    }

    [Fact]
    public void Derived_age_and_name_follow_their_sources()
    {
        using var s = NewSession();
        var p = s.GetAll(EntityKind.Player)[0];
        s.Set(p, "DateOfBirth", s.GameDate.AddYears(-17));
        Assert.Equal(17, s.Read(p, "Age"));
        s.Set(p, "CommonName", "Pelé Jr");
        Assert.Equal("Pelé Jr", s.Read(p, PropertyCatalog.DisplayName));
    }

    [Fact]
    public void Heal_and_remove_bans_presets_fix_status()
    {
        using var s = NewSession();
        var lib = new PresetLibrary(RepoFolder("presets"), Path.Combine(Path.GetTempPath(), "fmoe-none"));
        var p = s.GetAll(EntityKind.Player)[0];
        s.Set(p, "IsInjured", true);
        s.Set(p, "IsBanned", true);
        s.Set(p, "Condition", 4000);

        PresetEngine.Apply(s, lib.Presets.Single(x => x.Name == "Heal"), [p]);
        PresetEngine.Apply(s, lib.Presets.Single(x => x.Name == "Remove bans"), [p]);

        Assert.Equal(false, s.Read(p, "IsInjured"));
        Assert.Equal(false, s.Read(p, "IsBanned"));
        Assert.Equal(10000, s.Read(p, "Condition"));
    }

    [Fact]
    public void Wonderkid_only_raises_values()
    {
        using var s = NewSession();
        var lib = new PresetLibrary(RepoFolder("presets"), Path.Combine(Path.GetTempPath(), "fmoe-none"));
        var p = s.GetAll(EntityKind.Player)[0];
        s.Set(p, "PotentialAbility", 200);
        s.Set(p, "Attr.InjuryProneness", 15);

        PresetEngine.Apply(s, lib.Presets.Single(x => x.Name == "Make WonderKid"), [p]);

        Assert.Equal(200, s.Read(p, "PotentialAbility"));
        Assert.Equal(20, s.Read(p, "Attr.Determination"));
        Assert.Equal(2, s.Read(p, "Attr.InjuryProneness"));
    }

    [Fact]
    public void Remove_brexit_applies_to_uk_nations_without_a_selection()
    {
        using var s = NewSession();
        var lib = new PresetLibrary(RepoFolder("presets"), Path.Combine(Path.GetTempPath(), "fmoe-none"));
        var result = PresetEngine.Apply(s, lib.Presets.Single(x => x.Name == "Remove Brexit"), []);

        var england = s.GetAll(EntityKind.Nation).Single(n => (string?)s.Read(n, "Name") == "England");
        var brazil = s.GetAll(EntityKind.Nation).Single(n => (string?)s.Read(n, "Name") == "Brazil");
        Assert.Equal(true, s.Read(england, "Rules.EuFreeMovement"));
        Assert.Equal(false, s.Read(brazil, "Rules.EuFreeMovement"));
        Assert.Equal(2, result.Entities); // England and Scotland exist in the demo
    }

    [Fact]
    public void Every_shipped_preset_loads_and_uses_known_properties()
    {
        var lib = new PresetLibrary(RepoFolder("presets"), Path.Combine(Path.GetTempPath(), "fmoe-none"));
        Assert.Empty(lib.Errors);
        Assert.NotEmpty(lib.Presets);
        foreach (var preset in lib.Presets)
        {
            foreach (var a in preset.Actions)
            {
                if (a.Custom is { } c) Assert.NotNull(CustomActions.Find(c));
                else Assert.NotEmpty(PresetEngine.ResolveKeys(preset.Target, a));
                if (a.FromProperty is { } from) Assert.NotNull(PropertyCatalog.Find(preset.Target, from));
            }
            foreach (var f in preset.Where) Assert.NotNull(PropertyCatalog.Find(preset.Target, f.Property));
        }
    }

    [Fact]
    public void Mass_edit_and_release_on_free()
    {
        using var s = NewSession();
        var squad = s.GetAll(EntityKind.Player).Take(5).ToList();
        PresetEngine.MassEdit(s, squad, "Contract.Wage", PresetOp.Multiply, 2);
        Assert.All(squad, p => Assert.Equal((long)s.Database.Read(p, "Contract.Wage")! * 2, s.Read(p, "Contract.Wage")));

        var p0 = squad[0];
        CustomActions.Find("ReleaseOnFree")!.Apply(s, p0, null);
        Assert.Null(s.Read(p0, "Contract.Club"));
        Assert.Equal(0L, s.Read(p0, "Contract.Wage"));
    }

    [Fact]
    public void Advanced_search_combines_text_and_filters()
    {
        using var s = NewSession();
        var young = SearchEngine.Run(s, new SearchQuery
        {
            Kind = EntityKind.Player,
            Filters = [new Filter("Age", FilterOp.AtMost, 19), new Filter("PotentialAbility", FilterOp.AtLeast, 120)],
        });
        Assert.NotEmpty(young);
        Assert.All(young, p => Assert.True((int)s.Read(p, "Age")! <= 19 && (int)s.Read(p, "PotentialAbility")! >= 120));

        var target = s.GetAll(EntityKind.Player)[10];
        SearchEngine.Run(s, new SearchQuery { Kind = EntityKind.Player, Text = "x" }); // fills the name cache a rename must clear
        s.Set(target, "LastName", "Fernández");
        s.Set(target, "FirstName", "Íker");
        var byName = SearchEngine.Run(s, new SearchQuery { Kind = EntityKind.Player, Text = "iker fernandez" });
        Assert.Contains(target, byName);

        var expiring = SearchEngine.Run(s, new SearchQuery
        {
            Kind = EntityKind.Player,
            Filters = [new Filter("Contract.Expiry", FilterOp.AtMost, s.GameDate.AddYears(1))],
        });
        Assert.All(expiring, p => Assert.True((DateOnly)s.Read(p, "Contract.Expiry")! <= s.GameDate.AddYears(1)));
    }

    [Fact]
    public void Freeze_reverts_and_only_up_keeps_gains()
    {
        using var s = NewSession();
        var a = s.GetAll(EntityKind.Player)[0];
        var b = s.GetAll(EntityKind.Player)[1];
        s.Database.Write(a, "Attr.Pace", 12);
        s.Database.Write(b, "Attr.Pace", 12);
        s.Watcher.WatchAttributes(a, WatchMode.Freeze);
        s.Watcher.WatchAttributes(b, WatchMode.OnlyUp);

        // The "game" changes things.
        s.Database.Write(a, "Attr.Pace", 14);
        s.Database.Write(b, "Attr.Pace", 14);
        s.Watcher.Tick();
        Assert.Equal(12, s.Database.Read(a, "Attr.Pace"));
        Assert.Equal(14, s.Database.Read(b, "Attr.Pace"));

        s.Database.Write(b, "Attr.Pace", 9);
        s.Watcher.Tick();
        Assert.Equal(14, s.Database.Read(b, "Attr.Pace"));
    }

    [Fact]
    public void Shipped_definitions_load_and_only_reference_catalog_keys()
    {
        var lib = DefinitionLibrary.LoadFolder(RepoFolder("definitions"));
        Assert.Empty(lib.Errors);
        foreach (var def in lib.Definitions)
        {
            foreach (var (kind, fields) in def.Structs)
            {
                var k = Enum.Parse<EntityKind>(kind);
                foreach (var key in fields.Keys.Where(k2 => k2 != "Id"))
                    Assert.True(PropertyCatalog.Find(k, key) is not null, $"{def.SourcePath}: {kind}.{key}");
            }
        }
    }

    /// <summary>
    /// Pictures are looked up by Id, so Id must be the game's unique ID (the number graphics packs
    /// and other editors use), not the record's row index. In FM27 the row index sits at +0x8 and
    /// the unique ID at +0xC; reading +0x8 gave every club, league, nation and face someone else's picture.
    /// </summary>
    [Fact]
    public void Fm27_ids_are_the_unique_ids_that_graphics_packs_use()
    {
        var def = DefinitionLibrary.LoadFolder(RepoFolder("definitions")).Definitions.Single(d => d.Version == "27.0.1");
        foreach (var kind in Enum.GetValues<EntityKind>())
            Assert.Equal(0xC, GameDefinition.ParseOffset(def.Structs[kind.ToString()]["Id"].Offset!));
    }
}
