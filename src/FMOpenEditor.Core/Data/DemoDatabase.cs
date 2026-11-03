using FMOpenEditor.Core.Model;

namespace FMOpenEditor.Core.Data;

/// <summary>
/// A generated, fictional save so the whole editor can be used, tested and developed without the
/// game running. Every catalog property is supported. Same seed, same world.
/// </summary>
public sealed class DemoDatabase : IGameDatabase
{
    private readonly Dictionary<EntityKind, List<Entity>> _entities = [];
    private readonly Dictionary<(EntityKind, int), Dictionary<string, object?>> _values = [];
    private readonly Random _rng;

    public string DisplayName => "Demo save (fictional data)";
    public bool IsLive => false;
    public DateOnly GameDate { get; } = new(2026, 7, 1);

    private static readonly (string Name, int Continent, int Reputation, bool Eu)[] NationSeeds =
    [
        ("England", 0, 9000, false), ("Spain", 0, 9200, true), ("Germany", 0, 9000, true), ("Italy", 0, 8800, true),
        ("France", 0, 9100, true), ("Portugal", 0, 8500, true), ("Netherlands", 0, 8400, true), ("Brazil", 1, 9300, false),
        ("Argentina", 1, 9200, false), ("Uruguay", 1, 7800, false), ("Colombia", 1, 7400, false), ("USA", 2, 7000, false),
        ("Mexico", 2, 7200, false), ("Nigeria", 3, 7000, false), ("Senegal", 3, 7300, false), ("Ghana", 3, 6800, false),
        ("Japan", 4, 7100, false), ("South Korea", 4, 6900, false), ("Australia", 5, 6500, false), ("Scotland", 0, 6600, false),
        ("Belgium", 0, 8600, true), ("Croatia", 0, 8200, true), ("Poland", 0, 7300, true), ("Norway", 0, 7000, false),
    ];

    private static readonly string[] FirstNames =
    [
        "Adrián", "Bruno", "Callum", "Dario", "Emre", "Felipe", "Gabriel", "Hugo", "Iker", "Jonas", "Kai", "Lucas",
        "Mateo", "Nico", "Oscar", "Pablo", "Rafael", "Samuel", "Tomás", "Umar", "Victor", "William", "Yusuf", "Zane",
        "Aitor", "Bastian", "Diego", "Enzo", "Finn", "Joel", "Kenji", "Leon", "Marco", "Noah", "Ollie", "Pedro",
        "Ruben", "Sami", "Theo", "Unai", "Wesley", "Xavi", "Youssef", "Ibrahim", "Mario", "Lars", "Min-jun", "Kofi",
    ];

    private static readonly string[] LastNames =
    [
        "Alvarez", "Becker", "Costa", "Diallo", "Eriksen", "Fernández", "García", "Hughes", "Ivanović", "Jensen",
        "Kowalski", "López", "Moreau", "Novak", "Okafor", "Pereira", "Quinn", "Rossi", "Silva", "Tanaka", "Ueda",
        "Van Dijkstra", "Walker", "Yilmaz", "Zielinski", "Mendes", "Navarro", "Ortega", "Petrov", "Romero", "Santos",
        "Torres", "Vidal", "Weber", "Mensah", "Kim", "Sato", "Murphy", "Campbell", "Dubois", "Bianchi", "Haaland",
    ];

    private static readonly string[] ClubPrefixes =
    [
        "Northbridge", "Riverside", "Ashford", "Kingsport", "Westvale", "Redmoor", "Harbour City", "Stonegate",
        "Elmwood", "Ironbridge", "Lakeshore", "Highfield", "Brookhaven", "Oakridge", "Silverton", "Fairhaven",
    ];

    private static readonly string[] ClubSuffixes = ["FC", "United", "Athletic", "City", "Rovers", "Sporting", "Town", "Albion"];

    private static readonly (string Name, int Nation, int Reputation)[] LeagueSeeds =
    [
        ("Premier Division", 0, 9500), ("Primera Liga", 1, 9300), ("Bundesklasse", 2, 9000),
        ("Serie Alta", 3, 8800), ("Ligue Première", 4, 8700), ("Liga Lusa", 5, 7800),
    ];

    private static readonly string[] Colours = ["#C8102E", "#0057B8", "#FFFFFF", "#000000", "#FFD100", "#00843D", "#6CACE4", "#7A263A"];

    public DemoDatabase(int seed = 27)
    {
        _rng = new Random(seed);
        foreach (var k in Enum.GetValues<EntityKind>()) _entities[k] = [];
        Generate();
    }

    public IReadOnlyList<Entity> GetAll(EntityKind kind) => _entities[kind];

    public Entity? Find(EntityKind kind, int id) =>
        id >= 1 && id <= _entities[kind].Count ? _entities[kind][id - 1] : null; // ids are 1-based positions

    public bool Supports(EntityKind kind, string key) => PropertyCatalog.Find(kind, key) is { IsDerived: false };

    public object? Read(Entity entity, string key) =>
        _values[(entity.Kind, entity.Id)].TryGetValue(key, out var v) ? v : Default(entity.Kind, key);

    public void Write(Entity entity, string key, object? value)
    {
        var def = PropertyCatalog.Get(entity.Kind, key);
        if (def.IsDerived) throw new InvalidOperationException($"{def.Label} is derived.");
        _values[(entity.Kind, entity.Id)][key] = Values.Coerce(def, value);
    }

    public void Refresh() { }

    public void Dispose() { }

    private static object? Default(EntityKind kind, string key)
    {
        var def = PropertyCatalog.Get(kind, key);
        return def.Kind switch
        {
            ValueKind.Text => "",
            ValueKind.Boolean => false,
            ValueKind.Date or ValueKind.Reference => null,
            ValueKind.Money => 0L,
            ValueKind.Decimal => 0d,
            _ => (int)Math.Max(0, def.Min),
        };
    }

    private Entity Add(EntityKind kind, Dictionary<string, object?> values)
    {
        var id = _entities[kind].Count + 1;
        var e = new Entity(kind, id, 0);
        _entities[kind].Add(e);
        _values[(kind, id)] = values;
        return e;
    }

    private T Pick<T>(IReadOnlyList<T> items) => items[_rng.Next(items.Count)];
    private int Between(int min, int max) => _rng.Next(min, max + 1);
    private int Clamp20(double v) => (int)Math.Clamp(Math.Round(v + _rng.NextDouble() * 4 - 2), 1, 20);

    private void Generate()
    {
        foreach (var n in NationSeeds)
        {
            Add(EntityKind.Nation, new()
            {
                ["Name"] = n.Name, ["Continent"] = n.Continent, ["Reputation"] = n.Reputation,
                ["Rules.EuFreeMovement"] = n.Eu, ["Rules.WorkPermitsForNonEu"] = n.Name is "England" or "Scotland",
                ["Rules.MaxNonEuInSquad"] = n.Eu ? 3 : 0,
            });
        }

        var clubNames = ClubPrefixes.SelectMany(p => ClubSuffixes.Select(s => $"{p} {s}")).OrderBy(_ => _rng.Next()).ToList();
        var nameIndex = 0;

        foreach (var league in LeagueSeeds)
        {
            var comp = Add(EntityKind.Competition, new()
            {
                ["Name"] = league.Name, ["ShortName"] = new string(league.Name.Where(char.IsUpper).ToArray()),
                ["Nation"] = league.Nation + 1, ["Reputation"] = league.Reputation,
                ["PrizeMoney"] = (long)league.Reputation * 10_000,
            });

            var table = new List<Entity>();
            for (var c = 0; c < 12; c++)
            {
                var strength = Math.Clamp(league.Reputation / 100 - c * 4 + Between(-6, 6), 30, 99);
                var name = clubNames[nameIndex++ % clubNames.Count];
                var played = Between(8, 12);
                var won = Math.Min(played, Math.Max(0, (int)(played * strength / 140.0) + Between(-1, 2)));
                var drawn = Math.Min(played - won, Between(0, 4));
                var club = Add(EntityKind.Club, new()
                {
                    ["Name"] = name,
                    ["ShortName"] = name.Split(' ')[0],
                    ["Nation"] = league.Nation + 1,
                    ["League"] = comp.Id,
                    ["Reputation"] = strength * 100,
                    ["TeamCohesion"] = Between(40, 90),
                    ["Finance.Balance"] = (long)strength * strength * Between(5_000, 30_000),
                    ["Finance.TransferBudget"] = (long)strength * strength * Between(1_000, 8_000),
                    ["Finance.WageBudget"] = (long)strength * Between(8_000, 20_000),
                    ["Finance.MaxWage"] = (long)strength * Between(2_000, 5_000),
                    ["Finance.TicketPrice"] = (long)Between(20, 90),
                    ["Finance.SeasonTicketPrice"] = (long)Between(300, 1500),
                    ["Facilities.Training"] = Math.Clamp(strength / 5 + Between(-2, 2), 1, 20),
                    ["Facilities.Youth"] = Math.Clamp(strength / 5 + Between(-3, 2), 1, 20),
                    ["Facilities.Corporate"] = Math.Clamp(strength / 5 + Between(-3, 2), 1, 20),
                    ["Facilities.YouthRecruitment"] = Math.Clamp(strength / 5 + Between(-3, 3), 1, 20),
                    ["Facilities.JuniorCoaching"] = Math.Clamp(strength / 5 + Between(-3, 3), 1, 20),
                    ["Kit.PrimaryColour"] = Pick(Colours),
                    ["Kit.SecondaryColour"] = Pick(Colours),
                    ["Stadium.Name"] = $"{name.Split(' ')[0]} Park",
                    ["Stadium.Capacity"] = strength * Between(300, 800),
                    ["Standing.Played"] = played,
                    ["Standing.Won"] = won,
                    ["Standing.Drawn"] = drawn,
                    ["Standing.Lost"] = played - won - drawn,
                    ["Standing.GoalsFor"] = won * 2 + drawn + Between(0, 6),
                    ["Standing.GoalsAgainst"] = (played - won - drawn) * 2 + drawn + Between(0, 5),
                    ["Standing.Points"] = won * 3 + drawn,
                });
                table.Add(club);
                GenerateSquad(club, strength, league.Nation + 1);
            }

            var pos = 1;
            foreach (var club in table.OrderByDescending(c => (int)Read(c, "Standing.Points")!))
            {
                _values[(EntityKind.Club, club.Id)]["Standing.Position"] = pos++;
            }
        }

        for (var i = 0; i < 60; i++) AddPlayer(null, Between(35, 65), Between(1, NationSeeds.Length));
    }

    private void GenerateSquad(Entity club, int strength, int nation)
    {
        for (var i = 0; i < 24; i++)
        {
            var home = _rng.NextDouble() < 0.55 ? nation : Between(1, NationSeeds.Length);
            AddPlayer(club, strength + Between(-15, 8), home, i < 3);
        }

        string[] roles = ["Manager", "Assistant Manager", "Coach", "Goalkeeping Coach", "Physio", "Scout"];
        foreach (var role in roles)
        {
            var ability = Math.Clamp(strength + Between(-10, 20), 40, 190);
            var values = new Dictionary<string, object?>
            {
                ["FirstName"] = Pick(FirstNames), ["LastName"] = Pick(LastNames),
                ["DateOfBirth"] = GameDate.AddYears(-Between(32, 66)).AddDays(-Between(0, 364)),
                ["Nation"] = _rng.NextDouble() < 0.7 ? nation : Between(1, NationSeeds.Length),
                ["Role"] = Array.IndexOf(PropertyCatalog.StaffRoles, role),
                ["CurrentAbility"] = ability, ["PotentialAbility"] = Math.Min(200, ability + Between(0, 15)),
                ["Reputation.Current"] = strength * 80, ["Reputation.Home"] = strength * 90, ["Reputation.World"] = strength * 60,
                ["Contract.Club"] = club.Id, ["Contract.Wage"] = (long)ability * Between(100, 400),
                ["Contract.Expiry"] = new DateOnly(GameDate.Year + Between(0, 3), 6, 30),
            };
            foreach (var p in PropertyCatalog.For(EntityKind.Staff).Where(p => p.IsAttribute))
                values[p.Key] = Clamp20(ability / 10.0);
            Add(EntityKind.Staff, values);
        }
    }

    private void AddPlayer(Entity? club, int strength, int nation, bool goalkeeper = false)
    {
        var age = Between(16, 36);
        var ca = Math.Clamp((int)(strength * 1.6) - (age < 21 ? (21 - age) * 7 : 0) + Between(-18, 12), 35, 190);
        var pa = Math.Clamp(ca + (age < 24 ? Between(10, 60) : Between(0, 10)), ca, 200);
        var level = ca / 10.0;

        var values = new Dictionary<string, object?>
        {
            ["FirstName"] = Pick(FirstNames),
            ["LastName"] = Pick(LastNames),
            ["CommonName"] = "",
            ["DateOfBirth"] = GameDate.AddYears(-age).AddDays(-Between(0, 364)),
            ["Nation"] = nation,
            ["SecondNation"] = _rng.NextDouble() < 0.15 ? Between(1, NationSeeds.Length) : null,
            ["Height"] = Between(165, 198),
            ["Weight"] = Between(62, 92),
            ["LeftFoot"] = Between(1, 20),
            ["RightFoot"] = Between(8, 20),
            ["CurrentAbility"] = ca,
            ["PotentialAbility"] = pa,
            ["Reputation.Current"] = ca * 40,
            ["Reputation.Home"] = ca * 45,
            ["Reputation.World"] = ca * 30,
            ["Value"] = (long)(Math.Pow(ca, 3) * (age < 24 ? 12 : 6)),
            ["Looks.SkinTone"] = Between(1, 20),
            ["Looks.HairColour"] = Between(1, 10),
            ["Looks.HairLength"] = Between(1, 5),
            ["Condition"] = Between(7500, 10000),
            ["MatchSharpness"] = Between(5000, 10000),
            ["Jadedness"] = Between(0, 3000),
            ["Morale"] = Between(6, 18),
            ["IsInjured"] = _rng.NextDouble() < 0.06,
            ["IsBanned"] = _rng.NextDouble() < 0.03,
            ["Contract.Club"] = club?.Id,
            ["Contract.Wage"] = club is null ? 0L : (long)(ca * ca * Between(2, 6)),
            ["Contract.Start"] = club is null ? null : new DateOnly(GameDate.Year - Between(0, 4), 7, 1),
            ["Contract.Expiry"] = club is null ? null : new DateOnly(GameDate.Year + Between(0, 5), 6, 30),
            ["Contract.SquadStatus"] = club is null ? 0 : Between(1, 8),
            ["Contract.ReleaseClause"] = _rng.NextDouble() < 0.3 ? (long)(Math.Pow(ca, 3) * 20) : 0L,
            ["Contract.Happiness"] = Between(30, 100),
            ["Contract.PlayingTimeHappiness"] = Between(30, 100),
            ["Contract.UnhappinessReasons"] = Between(0, 3),
            ["TrainedAtClub"] = age < 21 ? club?.Id : null,
            ["TrainedInNation"] = nation,
        };

        foreach (var p in PropertyCatalog.For(EntityKind.Player).Where(p => p.IsAttribute))
        {
            var cat = p.Category;
            values[p.Key] = cat switch
            {
                "Goalkeeping" => goalkeeper ? Clamp20(level) : Between(1, 6),
                "Positions" => 1,
                "Hidden" => Between(1, 20),
                _ => goalkeeper && cat == "Technical" ? Between(1, 9) : Clamp20(level),
            };
        }

        var position = goalkeeper ? "GK" : Pick(PropertyCatalog.PlayerPositions.Skip(1).ToArray());
        values[$"Pos.{position}"] = 20;

        Add(EntityKind.Player, values);
    }
}
