using System.Diagnostics;

namespace FMOpenEditor.Core.Memory;

/// <summary>A running Football Manager process and the native module that holds the game database.</summary>
public sealed record GameProcess(int ProcessId, string ProcessName, nint ModuleBase, int ModuleSize, string ModulePath, string ModuleVersion)
{
    /// <summary>Process names Football Manager has shipped under.</summary>
    public static readonly string[] ProcessNames = ["fm", "fm27", "football manager 2027"];

    /// <summary>
    /// The native simulation engine. Since FM moved to Unity the UI is IL2CPP, but players, clubs and
    /// competitions still live in this C++ module's heap.
    /// </summary>
    public const string DefaultModule = "game_plugin.dll";

    public static GameProcess? FindRunning(string moduleName = DefaultModule)
    {
        foreach (var name in ProcessNames)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                var found = TryDescribe(process, moduleName);
                if (found is not null) return found;
            }
        }
        return null;
    }

    private static GameProcess? TryDescribe(Process process, string moduleName)
    {
        try
        {
            foreach (ProcessModule module in process.Modules)
            {
                if (!string.Equals(module.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase)) continue;
                // ProductVersion carries the build ("27.0.0+2493426"); FileVersion is only "27.0".
                var info = module.FileVersionInfo;
                var version = info.ProductVersion ?? info.FileVersion ?? "unknown";
                return new GameProcess(process.Id, process.ProcessName, module.BaseAddress, module.ModuleMemorySize,
                    module.FileName, version);
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Process exited, or we lack rights (e.g. game running elevated and we're not).
        }
        return null;
    }
}
