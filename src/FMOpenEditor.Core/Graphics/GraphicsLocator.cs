using System.Text.RegularExpressions;

namespace FMOpenEditor.Core.Graphics;

/// <summary>
/// Finds the Football Manager user-data <c>graphics</c> folder on whatever machine the editor runs on.
/// Nothing here is tied to one user's paths: Documents is resolved through the OS (so OneDrive and
/// relocated Documents folders work), and every installed FM edition is discovered by name.
/// </summary>
public static partial class GraphicsLocator
{
    [GeneratedRegex(@"^Football Manager (\d{2,4})", RegexOptions.IgnoreCase)]
    private static partial Regex EditionName();

    /// <summary>
    /// The graphics folders to index, best match first.
    /// </summary>
    /// <param name="edition">Two-digit FM edition of the connected game ("27"), when known.</param>
    /// <param name="userOverride">A folder the user picked in Settings; used on its own when it exists.</param>
    public static IReadOnlyList<string> Find(string? edition = null, string? userOverride = null)
    {
        if (!string.IsNullOrWhiteSpace(userOverride))
            return Directory.Exists(userOverride) ? [userOverride] : [];

        var found = new List<(int Edition, string Path)>();
        foreach (var si in SportsInteractiveFolders())
        {
            IEnumerable<string> games;
            try { games = Directory.EnumerateDirectories(si, "Football Manager*").ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

            foreach (var game in games)
            {
                var m = EditionName().Match(Path.GetFileName(game));
                if (!m.Success) continue;
                var graphics = Path.Combine(game, "graphics");
                if (!Directory.Exists(graphics)) continue;
                found.Add((int.Parse(m.Groups[1].Value) % 100, graphics));
            }
        }

        if (found.Count == 0) return [];

        // Graphics packs are per edition (ids are stable, but users keep one folder per game), so
        // only one edition's folders are used: the connected one, or else the newest installed.
        var wanted = int.TryParse(edition, out var e) && found.Any(f => f.Edition == e % 100)
            ? e % 100
            : found.Max(f => f.Edition);
        return found.Where(f => f.Edition == wanted).Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Every "Sports Interactive" user-data folder that exists, across platforms.</summary>
    public static IEnumerable<string> SportsInteractiveFolders()
    {
        var candidates = new List<string?>
        {
            // Windows (and the default everywhere .NET maps "Documents").
            Join(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sports Interactive"),
        };

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
        {
            // OneDrive sometimes owns Documents without the shell folder being redirected.
            candidates.Add(Join(Environment.GetEnvironmentVariable("OneDrive"), "Documents", "Sports Interactive"));
            candidates.Add(Join(home, "Documents", "Sports Interactive"));
            // macOS.
            candidates.Add(Join(home, "Library", "Application Support", "Sports Interactive"));
            // Linux native and common Steam locations.
            candidates.Add(Join(home, ".local", "share", "Sports Interactive"));
        }

        return candidates
            .Where(c => c is not null && Directory.Exists(c))
            .Select(c => Path.GetFullPath(c!))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string? Join(string? root, params string[] parts) =>
        string.IsNullOrEmpty(root) ? null : Path.Combine([root, .. parts]);
}
