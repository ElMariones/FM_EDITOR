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
    public void Jadedness_can_be_negative()
    {
        using var s = NewSession();
        var p = s.GetAll(EntityKind.Player)[0];
        s.Set(p, "Jadedness", -33);
        Assert.Equal(-33, s.Read(p, "Jadedness"));
    }

    private static readonly string[] Outfield = ["Technical", "Mental", "Physical"];

    private static IEnumerable<string> CountingAttributes() =>
        PropertyCatalog.For(EntityKind.Player).Where(d => d.IsAttribute && Outfield.Contains(d.Category)).Select(d => d.Key);

    private static Entity Outfielder(EditorSession s) =>
        s.GetAll(EntityKind.Player).First(p => (int)s.Read(p, "Pos.GK")! < 15);

    [Fact]
    public void Maxing_attributes_takes_current_ability_to_200_and_lifts_potential()
    {
        using var s = NewSession();
        var p = Outfielder(s);
        s.Set(p, "CurrentAbility", 120);
        s.Set(p, "PotentialAbility", 150);

        foreach (var key in CountingAttributes()) s.Set(p, key, 20);

        Assert.Equal(200, s.Read(p, "CurrentAbility"));
        Assert.Equal(200, s.Read(p, "PotentialAbility"));
    }

    [Fact]
    public void Current_ability_follows_attributes_and_potential_never_drops()
    {
        using var s = NewSession();
        var p = Outfielder(s);
        s.Set(p, "CurrentAbility", 100);
        s.Set(p, "PotentialAbility", 180);
        var pace = (int)s.Read(p, "Attr.Pace")!;
        var raised = Math.Min(20, pace + 5);

        s.Set(p, "Attr.Pace", raised);
        var ca = (int)s.Read(p, "CurrentAbility")!;
        Assert.True(ca > 100);
        Assert.Equal(180, s.Read(p, "PotentialAbility"));

        // Putting it back restores current ability exactly; potential stays where it was.
        s.Set(p, "Attr.Pace", pace);
        Assert.Equal(100, s.Read(p, "CurrentAbility"));
        Assert.Equal(180, s.Read(p, "PotentialAbility"));

        // Hidden attributes don't count.
        s.Set(p, "Attr.Consistency", 20);
        Assert.Equal(100, s.Read(p, "CurrentAbility"));
    }

    /// <summary>The demo data priced with FM27's own ability weights.</summary>
    private sealed class Priced(IGameDatabase inner) : IGameDatabase
    {
        public static readonly IReadOnlyDictionary<string, AbilityWeightsDef> Fm27 =
            GameDefinition.Load(Path.Combine(RepoFolder("definitions"), "fm27-27.0.1.json")).AbilityWeights;
        public IReadOnlyDictionary<string, AbilityWeightsDef>? AbilityWeights => Fm27;
        public string DisplayName => inner.DisplayName;
        public bool IsLive => false;
        public DateOnly GameDate => inner.GameDate;
        public IReadOnlyList<Entity> GetAll(EntityKind kind) => inner.GetAll(kind);
        public Entity? Find(EntityKind kind, int id) => inner.Find(kind, id);
        public bool Supports(EntityKind kind, string key) => inner.Supports(kind, key);
        public object? Read(Entity entity, string key) => inner.Read(entity, key);
        public void Write(Entity entity, string key, object? value) => inner.Write(entity, key, value);
        public void Refresh() => inner.Refresh();
        public void Dispose() => inner.Dispose();
    }

    /// <summary>A player whose only natural position is <paramref name="position"/>, with mid-range attributes.</summary>
    private static Entity OnlyAt(EditorSession s, string position)
    {
        var p = Outfielder(s);
        s.AutoAbility = false;
        foreach (var k in Priced.Fm27.Keys) s.Set(p, k, k == position ? 20 : 1);
        foreach (var k in Priced.Fm27.Values.SelectMany(w => w.Weights.Keys).Distinct()) s.Set(p, k, 10);
        foreach (var k in CountingAttributes()) s.Set(p, k, 10);
        s.Set(p, "CurrentAbility", 100);
        s.Set(p, "PotentialAbility", 180);
        s.AutoAbility = true;
        return p;
    }

    [Fact]
    public void Ability_is_priced_with_the_games_weights_for_the_position()
    {
        using var s = new EditorSession(new Priced(new DemoDatabase()));
        var p = OnlyAt(s, "Pos.ST");
        var w = Priced.Fm27["Pos.ST"].Weights;

        s.Set(p, "Attr.Finishing", 15);
        Assert.Equal((int)Math.Round(100 + 5 * w["Attr.Finishing"]), s.Read(p, "CurrentAbility"));
        s.Set(p, "Attr.Finishing", 10);
        Assert.Equal(100, s.Read(p, "CurrentAbility"));

        // Pace costs a striker far more than corners; hidden attributes cost nothing.
        s.Set(p, "Attr.Pace", 15);
        var pace = (int)s.Read(p, "CurrentAbility")! - 100;
        s.Set(p, "Attr.Pace", 10);
        s.Set(p, "Attr.Corners", 15);
        var corners = (int)s.Read(p, "CurrentAbility")! - 100;
        s.Set(p, "Attr.Corners", 10);
        Assert.True(pace > 3 * corners, $"pace +{pace}, corners +{corners}");
        s.Set(p, "Attr.Consistency", 20);
        Assert.Equal(100, s.Read(p, "CurrentAbility"));

        // His weaker foot counts too.
        s.Set(p, "LeftFoot", 15);
        Assert.True((int)s.Read(p, "CurrentAbility")! > 100);
    }

    [Fact]
    public void A_player_is_priced_at_his_dearest_natural_position()
    {
        using var s = new EditorSession(new Priced(new DemoDatabase()));
        var p = OnlyAt(s, "Pos.DC");
        var dc = Priced.Fm27["Pos.DC"].Weights;
        var st = Priced.Fm27["Pos.ST"].Weights;

        // Finishing barely matters to a centre-back...
        s.Set(p, "Attr.Finishing", 20);
        var asDefender = (int)s.Read(p, "CurrentAbility")!;
        Assert.Equal((int)Math.Round(100 + 10 * dc["Attr.Finishing"]), asDefender);

        // ...but once he is a natural striker as well, the striker's price of it is the one that counts.
        s.Set(p, "Pos.ST", 20);
        Assert.True((int)s.Read(p, "CurrentAbility")! > asDefender);
        Assert.True(st["Attr.Finishing"] > dc["Attr.Finishing"]);

        // Back to centre-back only: back where he was.
        s.Set(p, "Pos.ST", 1);
        Assert.Equal(asDefender, s.Read(p, "CurrentAbility"));
    }

    [Fact]
    public void Every_position_has_weights_and_hidden_attributes_cost_nothing()
    {
        foreach (var pos in new[] { "GK", "DL", "DC", "DR", "WBL", "WBR", "DM", "ML", "MC", "MR", "AML", "AMC", "AMR", "ST" })
        {
            var w = Priced.Fm27["Pos." + pos];
            Assert.True(w.Weights["Attr.Decisions"] > 0.5, pos);
            Assert.DoesNotContain("Attr.Consistency", w.Weights.Keys);
            Assert.DoesNotContain("Attr.Determination", w.Weights.Keys);
            Assert.All(w.Weights.Keys, k => Assert.True(PropertyCatalog.Find(EntityKind.Player, k) is not null, k));
        }
    }

    [Fact]
    public void Copy_and_paste_stats_moves_abilities_and_nothing_else()
    {
        using var s = new EditorSession(new Priced(new DemoDatabase()));
        var players = s.GetAll(EntityKind.Player);
        var (from, to) = (players[0], players[1]);
        s.AutoAbility = false;
        s.Set(from, "CurrentAbility", 157);
        s.Set(from, "PotentialAbility", 181);
        s.Set(from, "Attr.Finishing", 19);
        s.Set(from, "Pos.ST", 20);
        s.Set(to, "Attr.Finishing", 4);
        s.AutoAbility = true;
        var name = s.Read(to, "LastName");
        var birth = s.Read(to, "DateOfBirth");
        var club = s.Read(to, "Contract.Club");
        var reputation = s.Read(to, "Reputation.Current");

        s.CopiedStats = StatsCopy.Copy(s, from);
        Assert.DoesNotContain("LastName", s.CopiedStats.Values.Keys);
        Assert.DoesNotContain("Contract.Club", s.CopiedStats.Values.Keys);
        Assert.Contains("Pos.ST", s.CopiedStats.Values.Keys);
        Assert.Contains("RightFoot", s.CopiedStats.Values.Keys);

        Assert.True(StatsCopy.Paste(s, to, s.CopiedStats) > 0);
        foreach (var (key, value) in s.CopiedStats.Values) Assert.Equal(value, s.Read(to, key));
        Assert.Equal(157, s.Read(to, "CurrentAbility"));   // as copied, not re-priced
        Assert.Equal(181, s.Read(to, "PotentialAbility"));
        Assert.Equal(name, s.Read(to, "LastName"));
        Assert.Equal(birth, s.Read(to, "DateOfBirth"));
        Assert.Equal(club, s.Read(to, "Contract.Club"));
        Assert.Equal(reputation, s.Read(to, "Reputation.Current"));
        Assert.True(s.AutoAbility);
        Assert.Equal(0, StatsCopy.Paste(s, to, s.CopiedStats));
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
