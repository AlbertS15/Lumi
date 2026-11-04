using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Media.Imaging;

namespace Lumi;

/// Finds the picture for a .desktop file's Icon= value, as the freedesktop icon theme spec describes:
/// an absolute path, or a name looked up in the current theme, its parents, hicolor and /usr/share/pixmaps.
public static class Icons
{
    private static readonly string[] Exts = { ".png", ".svg", ".xpm" };
    private static readonly Dictionary<string, Bitmap?> Cache = new();
    private static List<string>? _roots;
    private static List<string>? _themes;

    /// A bitmap for the icon name, or null when nothing usable is found (the caller then shows a letter).
    public static Bitmap? Load(string icon)
    {
        if (string.IsNullOrWhiteSpace(icon)) return null;
        lock (Cache)
        {
            if (Cache.TryGetValue(icon, out var hit)) return hit;
            Bitmap? bmp = null;
            try
            {
                var file = Find(icon);
                if (file != null) bmp = Open(file);
            }
            catch { }
            Cache[icon] = bmp;
            return bmp;
        }
    }

    private static Bitmap? Open(string file)
    {
        if (file.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return new Bitmap(file);
        if (file.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) return FromSvg(file);
        return null; // .xpm: Avalonia can't read it
    }

    /// Avalonia has no SVG support of its own, so rasterise through rsvg-convert when it's installed.
    private static Bitmap? FromSvg(string file)
    {
        var rsvg = Tools.Find("rsvg-convert");
        if (rsvg == null) return null;
        var tmp = Path.Combine(Path.GetTempPath(), $"lumi-icon-{Environment.UserName}-{Guid.NewGuid():N}.png");
        try
        {
            var psi = new ProcessStartInfo(rsvg) { UseShellExecute = false, RedirectStandardError = true };
            foreach (var a in new[] { "-w", "64", "-h", "64", "-o", tmp, file }) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p == null) return null;
            p.StandardError.ReadToEnd();
            if (!p.WaitForExit(3000)) { try { p.Kill(); } catch { } return null; }
            return p.ExitCode == 0 && File.Exists(tmp) ? new Bitmap(tmp) : null;
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }

    private static string? Find(string icon)
    {
        if (Path.IsPathRooted(icon)) return File.Exists(icon) ? icon : null;
        // "firefox.png" in Icon= is allowed by old files; the name is then "firefox".
        var name = Exts.Any(e => icon.EndsWith(e, StringComparison.OrdinalIgnoreCase)) ? Path.GetFileNameWithoutExtension(icon) : icon;

        foreach (var theme in Themes())
        foreach (var root in Roots())
        {
            var dir = Path.Combine(root, "icons", theme);
            if (!Directory.Exists(dir)) continue;
            if (BestInTheme(dir, name) is { } f) return f;
        }
        foreach (var root in Roots())
        {
            foreach (var ext in Exts)
            {
                var f = Path.Combine(root, "pixmaps", name + ext);
                if (File.Exists(f)) return f;
                f = Path.Combine(root, "icons", name + ext); // loose files directly in icons/
                if (File.Exists(f)) return f;
            }
        }
        return null;
    }

    /// Looks in every size folder (48x48, scalable, 48x48@2, apps/48 …) and picks the one nearest 48px, png before svg.
    private static string? BestInTheme(string themeDir, string name)
    {
        string? best = null;
        var bestScore = int.MaxValue;
        IEnumerable<string> subdirs;
        try { subdirs = Directory.EnumerateDirectories(themeDir).ToList(); }
        catch { return null; }
        foreach (var sizeDir in subdirs)
        {
            var folder = Path.GetFileName(sizeDir);
            if (folder.Contains("symbolic")) continue; // monochrome UI glyphs, not app icons
            // Layouts: <size>/apps (hicolor, Adwaita) or apps/<size> (Papirus, Yaru).
            var candidates = new List<(string dir, string size)> { (Path.Combine(sizeDir, "apps"), folder) };
            if (folder == "apps")
            {
                try { candidates.AddRange(Directory.EnumerateDirectories(sizeDir).Select(d => (d, Path.GetFileName(d)))); }
                catch { }
                candidates.Add((sizeDir, "scalable"));
            }
            foreach (var (dir, size) in candidates)
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var ext in new[] { ".png", ".svg" })
                {
                    var f = Path.Combine(dir, name + ext);
                    if (!File.Exists(f)) continue;
                    var score = Score(size, ext);
                    if (score < bestScore) { bestScore = score; best = f; }
                }
            }
        }
        return best;
    }

    private const int Wanted = 48;

    /// Lower is better: sizes at or above 48 beat smaller ones, svg only when no png is close, 2x folders count as double.
    private static int Score(string size, string ext)
    {
        var px = 0;
        var scale = 1;
        var at = size.IndexOf('@');
        if (at >= 0) { int.TryParse(size[(at + 1)..], out scale); size = size[..at]; if (scale < 1) scale = 1; }
        var x = size.IndexOf('x');
        if (x > 0) int.TryParse(size[..x], out px);
        else int.TryParse(size, out px);
        if (size == "scalable") px = 0;
        var svg = ext == ".svg" || px == 0;
        if (svg) return 500; // after any png from 16 up
        px *= scale;
        if (px < 24) return 1000 + (Wanted - px);
        return px >= Wanted ? px - Wanted : (Wanted - px) * 4 + 100;
    }

    private static List<string> Roots()
    {
        if (_roots != null) return _roots;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } d ? d : Path.Combine(home, ".local", "share");
        var dataDirs = (Environment.GetEnvironmentVariable("XDG_DATA_DIRS") is { Length: > 0 } dirs ? dirs : "/usr/local/share:/usr/share").Split(':', StringSplitOptions.RemoveEmptyEntries);
        var roots = new List<string> { dataHome };
        roots.AddRange(dataDirs);
        roots.Add("/usr/share");
        roots.Add("/var/lib/flatpak/exports/share");
        roots.Add(Path.Combine(home, ".local/share/flatpak/exports/share"));
        roots.Add("/var/lib/snapd/desktop");
        return _roots = roots.Distinct().Where(Directory.Exists).ToList();
    }

    /// The current GNOME icon theme first, then what it inherits, then hicolor.
    private static List<string> Themes()
    {
        if (_themes != null) return _themes;
        var themes = new List<string>();
        var current = Gsettings("org.gnome.desktop.interface", "icon-theme");
        if (current != null) AddWithParents(themes, current, 0);
        foreach (var t in new[] { "Adwaita", "hicolor" }) AddWithParents(themes, t, 0);
        if (!themes.Contains("hicolor")) themes.Add("hicolor");
        return _themes = themes;
    }

    private static void AddWithParents(List<string> list, string theme, int depth)
    {
        if (list.Contains(theme) || depth > 5) return;
        list.Add(theme);
        foreach (var root in Roots())
        {
            var index = Path.Combine(root, "icons", theme, "index.theme");
            if (!File.Exists(index)) continue;
            try
            {
                var inherits = File.ReadLines(index).FirstOrDefault(l => l.StartsWith("Inherits="));
                if (inherits == null) continue;
                foreach (var p in inherits["Inherits=".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    if (p != "hicolor") AddWithParents(list, p, depth + 1);
            }
            catch { }
            break;
        }
    }

    private static string? Gsettings(string schema, string key)
    {
        try
        {
            var gs = Tools.Find("gsettings");
            if (gs == null) return null;
            var psi = new ProcessStartInfo(gs) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            psi.ArgumentList.Add("get"); psi.ArgumentList.Add(schema); psi.ArgumentList.Add(key);
            using var p = Process.Start(psi);
            if (p == null) return null;
            var o = p.StandardOutput.ReadToEnd().Trim().Trim('\'');
            if (!p.WaitForExit(1500)) { try { p.Kill(); } catch { } return null; }
            return o.Length > 0 && p.ExitCode == 0 ? o : null;
        }
        catch { return null; }
    }
}
