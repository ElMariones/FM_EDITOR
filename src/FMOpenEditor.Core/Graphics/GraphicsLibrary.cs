using System.Globalization;
using FMOpenEditor.Core.Model;

namespace FMOpenEditor.Core.Graphics;

/// <summary>How large the picture will be shown, which decides the slot that is preferred.</summary>
public enum GraphicSize
{
    /// <summary>List rows and chips: club/nation/competition icons, faces.</summary>
    Small,

    /// <summary>Headers and hero areas: full logos and portraits.</summary>
    Large,
}

/// <summary>
/// An index of the user's Football Manager graphics folder (faces, logos and any other pack).
/// <para>
/// FM packs are folders holding pictures and a <c>config.xml</c> whose records map a file to a
/// place in the game's resource tree: <c>&lt;record from="face_123" to="graphics/pictures/person/123/portrait"/&gt;</c>.
/// The folder can be nested anywhere under <c>graphics</c>, so the whole tree is walked and every
/// config is read. Nothing is assumed about pack names or layout, which is what makes custom
/// facepacks and logo packs work without configuration.
/// </para>
/// </summary>
public sealed class GraphicsLibrary
{
    private static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".webp", ".bmp"];

    /// <summary>A folder plus the text that precedes the id in its file names ("face_", "images/").</summary>
    private readonly List<(string Directory, string Prefix)> _sources = [];
    private readonly Dictionary<(string Directory, string Prefix), int> _sourceIndex = [];

    /// <summary>Per "type/slot" (e.g. "person/portrait"): id → index into <see cref="_sources"/>.</summary>
    private readonly Dictionary<string, Dictionary<long, int>> _byPattern = new(StringComparer.Ordinal);

    /// <summary>Records whose file name doesn't end in the id, keyed like the above; value is the path without extension.</summary>
    private readonly Dictionary<string, Dictionary<long, string>> _explicit = new(StringComparer.Ordinal);

    public static GraphicsLibrary Empty { get; } = new();

    /// <summary>The graphics folders that were indexed.</summary>
    public IReadOnlyList<string> Roots { get; private set; } = [];

    /// <summary>How many config.xml files contributed mappings.</summary>
    public int PackCount { get; private set; }

    public int Count => _byPattern.Values.Sum(d => d.Count) + _explicit.Values.Sum(d => d.Count);

    public bool IsEmpty => _byPattern.Count == 0 && _explicit.Count == 0;

    /// <summary>Number of mapped pictures for a resource type ("person", "club", "comp", "nation", ...).</summary>
    public int CountOf(string type)
    {
        // A person usually has both a portrait and an icon; report the fullest slot, not the sum.
        var prefix = type + "/";
        var slots = _byPattern.Keys.Concat(_explicit.Keys).Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).Distinct();
        return slots.Select(k => (_byPattern.GetValueOrDefault(k)?.Count ?? 0) + (_explicit.GetValueOrDefault(k)?.Count ?? 0))
                    .DefaultIfEmpty(0).Max();
    }

    /// <summary>Whether any picture exists for this kind of entity; lets the UI drop the picture slot entirely.</summary>
    public bool HasAny(EntityKind kind) => CountOf(TypeOf(kind)) > 0;

    /// <summary>Walks <paramref name="roots"/> and indexes every pack found. Missing folders are skipped.</summary>
    public static GraphicsLibrary Load(IEnumerable<string> roots, CancellationToken cancel = default)
    {
        var lib = new GraphicsLibrary();
        var used = new List<string>();
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root)) continue;
            used.Add(root);
            lib.Walk(root, cancel);
        }
        lib.Roots = used;
        return lib;
    }

    private void Walk(string root, CancellationToken cancel)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancel.ThrowIfCancellationRequested();
            var dir = pending.Pop();
            try
            {
                var config = Path.Combine(dir, "config.xml");
                if (File.Exists(config) && ReadConfig(dir, config, cancel) > 0) PackCount++;
                // Sorted so that, when two packs map the same id, the result doesn't depend on disk order.
                foreach (var sub in Directory.EnumerateDirectories(dir).OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase))
                    pending.Push(sub);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // An unreadable pack shouldn't cost the user every other pack.
            }
        }
    }

    /// <summary>
    /// Reads one config.xml. These files reach 40+ MB for the big facepacks, so they are scanned as
    /// text rather than loaded as a DOM; the format is one self-closing record per mapping.
    /// </summary>
    private int ReadConfig(string dir, string configPath, CancellationToken cancel)
    {
        var added = 0;
        var inComment = false;
        var lineNo = 0;
        using var reader = new StreamReader(configPath);
        while (reader.ReadLine() is { } line)
        {
            if ((++lineNo & 0xFFFF) == 0) cancel.ThrowIfCancellationRequested();
            var span = StripComments(line, ref inComment);
            var pos = 0;
            while (TryReadRecord(span, ref pos, out var from, out var to))
                if (Add(dir, from, to)) added++;
        }
        return added;
    }

    private static string StripComments(string line, ref bool inComment)
    {
        if (!inComment && !line.Contains("<!--", StringComparison.Ordinal)) return line;
        var sb = new System.Text.StringBuilder(line.Length);
        var i = 0;
        while (i < line.Length)
        {
            if (inComment)
            {
                var end = line.IndexOf("-->", i, StringComparison.Ordinal);
                if (end < 0) return sb.ToString();
                inComment = false;
                i = end + 3;
            }
            else
            {
                var start = line.IndexOf("<!--", i, StringComparison.Ordinal);
                if (start < 0) { sb.Append(line, i, line.Length - i); break; }
                sb.Append(line, i, start - i);
                inComment = true;
                i = start + 4;
            }
        }
        return sb.ToString();
    }

    private static bool TryReadRecord(string s, ref int pos, out string from, out string to)
    {
        from = to = "";
        var start = s.IndexOf("<record", pos, StringComparison.Ordinal);
        if (start < 0) return false;
        var end = s.IndexOf('>', start);
        if (end < 0) { pos = s.Length; return false; }
        pos = end + 1;
        // The outer <record> wrapper has no attributes; it just yields empty strings and is ignored by Add.
        from = Attribute(s, start, end, "from");
        to = Attribute(s, start, end, "to");
        return true;
    }

    private static string Attribute(string s, int start, int end, string name)
    {
        var search = start;
        while (true)
        {
            var i = s.IndexOf(name, search, end - search, StringComparison.Ordinal);
            if (i < 0) return "";
            var j = i + name.Length;
            // Must be a whole attribute name: preceded by whitespace, followed by '='.
            if (char.IsWhiteSpace(s[i - 1]) && j < end && s[j] == '=' && j + 1 < end && s[j + 1] is '"' or '\'')
            {
                var quote = s[j + 1];
                var close = s.IndexOf(quote, j + 2);
                return close < 0 || close > end ? "" : s.Substring(j + 2, close - j - 2);
            }
            search = j;
        }
    }

    private bool Add(string dir, string from, string to)
    {
        if (from.Length == 0 || to.Length == 0) return false;

        // graphics/pictures/<type>/<id>/<slot>; anything else (default styles, kits, 3D) isn't ours.
        var parts = to.Split('/');
        if (parts.Length != 5 || parts[0] != "graphics" || parts[1] != "pictures") return false;
        if (!long.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var id)) return false;
        var key = parts[2] + "/" + parts[4];

        from = from.Replace('\\', '/');
        var idText = parts[3];
        if (from.EndsWith(idText, StringComparison.Ordinal))
        {
            var source = (dir, from[..^idText.Length]);
            if (!_sourceIndex.TryGetValue(source, out var index))
            {
                index = _sources.Count;
                _sources.Add(source);
                _sourceIndex[source] = index;
            }
            if (!_byPattern.TryGetValue(key, out var map)) _byPattern[key] = map = [];
            map[id] = index;
        }
        else
        {
            if (!_explicit.TryGetValue(key, out var map)) _explicit[key] = map = [];
            map[id] = Path.Combine(dir, from);
        }
        return true;
    }

    /// <summary>
    /// The picture file for a resource, trying each slot in order ("portrait", "icon", "logo", ...).
    /// Returns null when no pack maps it or the mapped file is missing.
    /// </summary>
    public string? Find(string type, long id, params string[] slots)
    {
        foreach (var slot in slots)
        {
            var key = type + "/" + slot;
            string? stem = null;
            if (_explicit.TryGetValue(key, out var exp) && exp.TryGetValue(id, out var path)) stem = path;
            else if (_byPattern.TryGetValue(key, out var map) && map.TryGetValue(id, out var index))
            {
                var (dir, prefix) = _sources[index];
                stem = Path.Combine(dir, prefix + id.ToString(CultureInfo.InvariantCulture));
            }
            if (stem is null) continue;
            if (Resolve(stem) is { } file) return file;
        }
        return null;
    }

    /// <summary>The picture for an entity: a face for people, a badge for clubs, competitions and nations.</summary>
    public string? Find(EntityKind kind, int id, GraphicSize size) => kind switch
    {
        // Icon faces are ~25px thumbnails, so the portrait is the better source even for small sizes.
        EntityKind.Player or EntityKind.Staff => Find("person", id, "portrait", "icon"),
        _ => size == GraphicSize.Large
            ? Find(TypeOf(kind), id, "logo", "icon")
            : Find(TypeOf(kind), id, "icon", "logo"),
    };

    public static string TypeOf(EntityKind kind) => kind switch
    {
        EntityKind.Player or EntityKind.Staff => "person",
        EntityKind.Club => "club",
        EntityKind.Competition => "comp",
        _ => "nation",
    };

    private static string? Resolve(string stem)
    {
        // Configs normally omit the extension; a few packs include it.
        if (Path.HasExtension(stem) && File.Exists(stem)) return stem;
        foreach (var ext in Extensions)
        {
            var candidate = stem + ext;
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
