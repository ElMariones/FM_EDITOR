namespace FMOpenEditor.Core.Model;

/// <summary>Every property the editor knows about, per entity kind, in display order.</summary>
public static class PropertyCatalog
{
    public static readonly string[] SquadStatuses =
    [
        "Not set", "Star Player", "Important Player", "Regular Starter", "Squad Player",
        "Fringe Player", "Hot Prospect", "Decent Young Player", "Not Needed",
    ];

    public static readonly string[] StaffRoles =
    [
        "Manager", "Assistant Manager", "Coach", "Goalkeeping Coach", "Fitness Coach", "Physio",
        "Sports Scientist", "Scout", "Analyst", "Director of Football", "Head of Youth Development", "Chairman",
    ];

    public static readonly string[] Continents =
    [
        "Europe", "South America", "North & Central America", "Africa", "Asia", "Oceania",
    ];

    public static readonly string[] PlayerPositions =
        ["GK", "DL", "DC", "DR", "WBL", "WBR", "DM", "ML", "MC", "MR", "AML", "AMC", "AMR", "ST"];

    public static readonly string[] TechnicalAttributes =
    [
        "Corners", "Crossing", "Dribbling", "Finishing", "First Touch", "Free Kicks", "Heading",
        "Long Shots", "Long Throws", "Marking", "Passing", "Penalty Taking", "Tackling", "Technique",
    ];

    public static readonly string[] MentalAttributes =
    [
        "Aggression", "Anticipation", "Bravery", "Composure", "Concentration", "Decisions", "Determination",
        "Flair", "Leadership", "Off The Ball", "Positioning", "Teamwork", "Vision", "Work Rate",
    ];

    public static readonly string[] PhysicalAttributes =
        ["Acceleration", "Agility", "Balance", "Jumping Reach", "Natural Fitness", "Pace", "Stamina", "Strength"];

    public static readonly string[] GoalkeepingAttributes =
    [
        "Aerial Reach", "Command Of Area", "Communication", "Eccentricity", "Handling", "Kicking",
        "One On Ones", "Punching", "Reflexes", "Rushing Out", "Throwing",
    ];

    public static readonly string[] HiddenAttributes =
    [
        "Adaptability", "Ambition", "Consistency", "Controversy", "Dirtiness", "Important Matches",
        "Injury Proneness", "Loyalty", "Pressure", "Professionalism", "Sportsmanship", "Temperament", "Versatility",
    ];

    public static readonly string[] StaffCoaching =
    [
        "Attacking", "Defending", "Fitness", "GK Distribution", "GK Handling", "GK Shot Stopping",
        "Mental", "Tactical", "Technical", "Working With Youngsters",
    ];

    public static readonly string[] StaffMental =
        ["Adaptability", "Determination", "Level Of Discipline", "Motivating", "People Management"];

    public static readonly string[] StaffKnowledge =
    [
        "Judging Player Ability", "Judging Player Potential", "Judging Staff Ability", "Negotiating",
        "Tactical Knowledge", "Physiotherapy", "Sports Science",
    ];

    private static readonly Dictionary<EntityKind, IReadOnlyList<PropertyDef>> ByKind = new()
    {
        [EntityKind.Player] = BuildPlayer(),
        [EntityKind.Staff] = BuildStaff(),
        [EntityKind.Club] = BuildClub(),
        [EntityKind.Competition] = BuildCompetition(),
        [EntityKind.Nation] = BuildNation(),
    };

    private static readonly Dictionary<(EntityKind, string), PropertyDef> ByKey =
        ByKind.SelectMany(kv => kv.Value.Select(p => (kv.Key, p)))
              .ToDictionary(t => (t.Key, t.p.Key), t => t.p);

    public static IReadOnlyList<PropertyDef> For(EntityKind kind) => ByKind[kind];

    public static PropertyDef? Find(EntityKind kind, string key) =>
        ByKey.TryGetValue((kind, key), out var def) ? def : null;

    public static PropertyDef Get(EntityKind kind, string key) =>
        Find(kind, key) ?? throw new KeyNotFoundException($"{kind} has no property '{key}'.");

    /// <summary>Turns a display name such as "First Touch" into a key part such as "FirstTouch".</summary>
    public static string KeyPart(string label) => new(label.Where(char.IsLetterOrDigit).ToArray());

    // Display name for every entity; derived so the grid and reference pickers can rely on it.
    public const string DisplayName = "FullName";

    private static PropertyDef Text(string key, string label, string cat) => new(key, label, cat, ValueKind.Text);
    private static PropertyDef Int(string key, string label, string cat, double min, double max) =>
        new(key, label, cat, ValueKind.Integer) { Min = min, Max = max };
    private static PropertyDef Money(string key, string label, string cat) =>
        new(key, label, cat, ValueKind.Money) { Min = -1_000_000_000_000, Max = 1_000_000_000_000 };
    private static PropertyDef Bool(string key, string label, string cat) => new(key, label, cat, ValueKind.Boolean);
    private static PropertyDef Date(string key, string label, string cat) => new(key, label, cat, ValueKind.Date);
    private static PropertyDef Ref(string key, string label, string cat, EntityKind to) =>
        new(key, label, cat, ValueKind.Reference) { RefKind = to };
    private static PropertyDef Choice(string key, string label, string cat, string[] choices) =>
        new(key, label, cat, ValueKind.Choice) { Choices = choices, Min = 0, Max = choices.Length - 1 };
    private static PropertyDef Derived(string key, string label, string cat, ValueKind kind) =>
        new(key, label, cat, kind) { IsDerived = true };

    private static IEnumerable<PropertyDef> Attributes(string prefix, string cat, IEnumerable<string> names, double max = 20) =>
        names.Select(n => new PropertyDef($"{prefix}.{KeyPart(n)}", n, cat, ValueKind.Integer)
        {
            Min = 1, Max = max, IsAttribute = true,
        });

    private static IEnumerable<PropertyDef> Reputation() =>
    [
        Int("Reputation.Current", "Current reputation", "Profile", 0, 10000),
        Int("Reputation.Home", "Home reputation", "Profile", 0, 10000),
        Int("Reputation.World", "World reputation", "Profile", 0, 10000),
    ];

    private static List<PropertyDef> BuildPlayer() =>
    [
        Derived(DisplayName, "Name", "Profile", ValueKind.Text),
        Text("FirstName", "First name", "Profile"),
        Text("LastName", "Last name", "Profile"),
        Text("CommonName", "Common name", "Profile"),
        Date("DateOfBirth", "Date of birth", "Profile"),
        Derived("Age", "Age", "Profile", ValueKind.Integer),
        Ref("Nation", "Nationality", "Profile", EntityKind.Nation),
        Ref("SecondNation", "Second nationality", "Profile", EntityKind.Nation),
        Int("Height", "Height (cm)", "Profile", 140, 220),
        Int("Weight", "Weight (kg)", "Profile", 40, 130),
        Int("LeftFoot", "Left foot", "Profile", 1, 20),
        Int("RightFoot", "Right foot", "Profile", 1, 20),
        Int("CurrentAbility", "Current ability", "Profile", 1, 200),
        new PropertyDef("PotentialAbility", "Potential ability", "Profile", ValueKind.Integer)
        {
            Min = -95, Max = 200,
            Description = "1-200, or -5 to -95 for a range the game rolls when the save starts.",
        },
        .. Reputation(),
        Money("Value", "Value", "Profile"),
        Ref("TrainedAtClub", "Trained at club", "Profile", EntityKind.Club),
        Ref("TrainedInNation", "Trained in nation", "Profile", EntityKind.Nation),
        Int("Looks.SkinTone", "Skin tone", "Looks", 1, 20),
        Int("Looks.HairColour", "Hair colour", "Looks", 1, 10),
        Int("Looks.HairLength", "Hair length", "Looks", 1, 5),

        .. Attributes("Attr", "Technical", TechnicalAttributes),
        .. Attributes("Attr", "Mental", MentalAttributes),
        .. Attributes("Attr", "Physical", PhysicalAttributes),
        .. Attributes("Attr", "Goalkeeping", GoalkeepingAttributes),
        .. Attributes("Attr", "Hidden", HiddenAttributes),
        .. Attributes("Pos", "Positions", PlayerPositions),

        Int("Condition", "Condition", "Status", 0, 10000),
        Int("MatchSharpness", "Match sharpness", "Status", 0, 10000),
        Int("Jadedness", "Jadedness", "Status", 0, 10000),
        Int("Morale", "Morale", "Status", 0, 20),
        Bool("IsInjured", "Injured", "Status"),
        Bool("IsBanned", "Banned", "Status"),
        Bool("IsRetiredFromInternational", "Retired from international football", "Status"),

        Ref("Contract.Club", "Club", "Contract", EntityKind.Club),
        Money("Contract.Wage", "Wage (per week)", "Contract"),
        Date("Contract.Start", "Contract start", "Contract"),
        Date("Contract.Expiry", "Contract expiry", "Contract"),
        Choice("Contract.SquadStatus", "Squad status", "Contract", SquadStatuses),
        Money("Contract.ReleaseClause", "Release clause", "Contract"),
        Int("Contract.Happiness", "Contract happiness", "Contract", 0, 100),
        Int("Contract.PlayingTimeHappiness", "Playing time happiness", "Contract", 0, 100),
        Int("Contract.UnhappinessReasons", "Unhappiness reasons", "Contract", 0, 20),
        Ref("Transfer.FutureClub", "Future club", "Contract", EntityKind.Club),
        Date("Transfer.FutureDate", "Future transfer date", "Contract"),
        Money("Transfer.FutureFee", "Future transfer fee", "Contract"),
    ];

    private static List<PropertyDef> BuildStaff() =>
    [
        Derived(DisplayName, "Name", "Profile", ValueKind.Text),
        Text("FirstName", "First name", "Profile"),
        Text("LastName", "Last name", "Profile"),
        Date("DateOfBirth", "Date of birth", "Profile"),
        Derived("Age", "Age", "Profile", ValueKind.Integer),
        Ref("Nation", "Nationality", "Profile", EntityKind.Nation),
        Choice("Role", "Role", "Profile", StaffRoles),
        Int("CurrentAbility", "Current ability", "Profile", 1, 200),
        Int("PotentialAbility", "Potential ability", "Profile", 1, 200),
        .. Reputation(),
        .. Attributes("Coach", "Coaching", StaffCoaching),
        .. Attributes("Mental", "Mental", StaffMental),
        .. Attributes("Knowledge", "Knowledge", StaffKnowledge),
        Ref("Contract.Club", "Club", "Contract", EntityKind.Club),
        Money("Contract.Wage", "Wage (per week)", "Contract"),
        Date("Contract.Expiry", "Contract expiry", "Contract"),
    ];

    private static List<PropertyDef> BuildClub() =>
    [
        Derived(DisplayName, "Name", "Profile", ValueKind.Text),
        Text("Name", "Name", "Profile"),
        Text("ShortName", "Short name", "Profile"),
        Ref("Nation", "Nation", "Profile", EntityKind.Nation),
        Ref("League", "League", "Profile", EntityKind.Competition),
        Int("Reputation", "Reputation", "Profile", 0, 10000),
        Int("TeamCohesion", "Team cohesion", "Profile", 0, 100),

        Money("Finance.Balance", "Balance", "Finances"),
        Money("Finance.TransferBudget", "Transfer budget", "Finances"),
        Money("Finance.WageBudget", "Wage budget (per week)", "Finances"),
        Money("Finance.MaxWage", "Maximum wage (per week)", "Finances"),
        Money("Finance.TicketPrice", "Ticket price", "Finances"),
        Money("Finance.SeasonTicketPrice", "Season ticket price", "Finances"),

        Int("Facilities.Training", "Training facilities", "Facilities", 1, 20),
        Int("Facilities.Youth", "Youth facilities", "Facilities", 1, 20),
        Int("Facilities.Corporate", "Corporate facilities", "Facilities", 1, 20),
        Int("Facilities.YouthRecruitment", "Youth recruitment", "Facilities", 1, 20),
        Int("Facilities.JuniorCoaching", "Junior coaching", "Facilities", 1, 20),

        Text("Kit.PrimaryColour", "Primary colour", "Kits & stadium"),
        Text("Kit.SecondaryColour", "Secondary colour", "Kits & stadium"),
        Text("Stadium.Name", "Stadium name", "Kits & stadium"),
        Int("Stadium.Capacity", "Stadium capacity", "Kits & stadium", 0, 200000),

        Int("Standing.Position", "League position", "League standing", 1, 40),
        Int("Standing.Played", "Played", "League standing", 0, 100),
        Int("Standing.Won", "Won", "League standing", 0, 100),
        Int("Standing.Drawn", "Drawn", "League standing", 0, 100),
        Int("Standing.Lost", "Lost", "League standing", 0, 100),
        Int("Standing.GoalsFor", "Goals for", "League standing", 0, 500),
        Int("Standing.GoalsAgainst", "Goals against", "League standing", 0, 500),
        Int("Standing.Points", "Points", "League standing", -100, 300),
    ];

    private static List<PropertyDef> BuildCompetition() =>
    [
        Derived(DisplayName, "Name", "Profile", ValueKind.Text),
        Text("Name", "Name", "Profile"),
        Text("ShortName", "Short name", "Profile"),
        Ref("Nation", "Nation", "Profile", EntityKind.Nation),
        Int("Reputation", "Reputation", "Profile", 0, 10000),
        Money("PrizeMoney", "Prize money (winner)", "Profile"),
    ];

    private static List<PropertyDef> BuildNation() =>
    [
        Derived(DisplayName, "Name", "Profile", ValueKind.Text),
        Text("Name", "Name", "Profile"),
        Choice("Continent", "Continent", "Profile", Continents),
        Int("Reputation", "Reputation", "Profile", 0, 10000),
        Bool("Rules.EuFreeMovement", "EU free movement", "Agreements"),
        Bool("Rules.WorkPermitsForNonEu", "Work permits for non-EU players", "Agreements"),
        Int("Rules.MaxNonEuInSquad", "Max non-EU players in matchday squad", "Agreements", 0, 25),
    ];
}
