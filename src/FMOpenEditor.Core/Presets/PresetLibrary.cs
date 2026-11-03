namespace FMOpenEditor.Core.Presets;

/// <summary>Built-in presets ship in the app's <c>presets</c> folder; users' own go in their app-data folder.</summary>
public sealed class PresetLibrary
{
    public List<Preset> Presets { get; } = [];
    public List<string> Errors { get; } = [];
    public string UserFolder { get; }

    public PresetLibrary(string builtInFolder, string userFolder)
    {
        UserFolder = userFolder;
        LoadFolder(builtInFolder, builtIn: true);
        LoadFolder(userFolder, builtIn: false);
        Presets.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Name, b.Name));
    }

    public static string DefaultUserFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FMOpenEditor", "presets");

    private void LoadFolder(string folder, bool builtIn)
    {
        if (!Directory.Exists(folder)) return;
        foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
        {
            try
            {
                var p = Preset.Load(file);
                p.IsBuiltIn = builtIn;
                Presets.Add(p);
            }
            catch (Exception ex)
            {
                Errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }
    }

    public string SaveUserPreset(Preset preset)
    {
        Directory.CreateDirectory(UserFolder);
        var safe = string.Concat(preset.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var path = Path.Combine(UserFolder, safe + ".json");
        preset.Save(path);
        preset.SourcePath = path;
        if (!Presets.Contains(preset)) Presets.Add(preset);
        return path;
    }
}
