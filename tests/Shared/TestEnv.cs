using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace Lumi.Tests;

/// Keeps the tests away from the real settings: Settings.Shared and anything reading %APPDATA% / ~/.config see an empty
/// temporary folder. Runs before any test touches Lumi's types.
public static class TestEnv
{
    public static readonly string Config = Path.Combine(Path.GetTempPath(), "lumi-tests-" + Guid.NewGuid().ToString("N"));

    [ModuleInitializer]
    internal static void Init()
    {
        Directory.CreateDirectory(Config);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Config);
        Environment.SetEnvironmentVariable("APPDATA", Config);
    }

    /// The repository root: the folder holding strings/strings.json.
    public static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "strings", "strings.json"))) dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("strings/strings.json not found above " + AppContext.BaseDirectory);
        }
    }

    public static string TempDir()
    {
        var dir = Path.Combine(Config, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
