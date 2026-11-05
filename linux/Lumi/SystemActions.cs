using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Lumi;

/// A system action shown as a row in the bar; it runs only on Enter or a click.
public sealed record ActionRow(string Icon, string Title, string Subtitle, Action Run);

/// System commands on Linux, through the desktop's standard tools: wpctl or pactl, gsettings, nmcli,
/// loginctl, and wmctrl for windows (X11 only). A command whose tool is missing shows no row.
public static class SystemActions
{
    /// The rows for what was typed: none when it isn't a command or this system can't do it.
    public static List<ActionRow> For(string text)
    {
        var rows = new List<ActionRow>();
        if (SystemCommands.Parse(text) is not { } command) return rows;
        switch (command.Kind)
        {
            case SystemActionKind.Volume when HasAudio:
                var level = command.Volume;
                rows.Add(new ActionRow("🔊", S.ActionVolume(level.ToString()), S.ActionNote, () => SetVolume(level)));
                break;
            case SystemActionKind.Mute when HasAudio:
                rows.Add(new ActionRow("🔇", S.ActionMute, S.ActionNote, () => SetMute(true)));
                break;
            case SystemActionKind.Unmute when HasAudio:
                rows.Add(new ActionRow("🔈", S.ActionUnmute, S.ActionNote, () => SetMute(false)));
                break;
            case SystemActionKind.DarkTheme when HasGnomeTheme:
                rows.Add(new ActionRow("🌙", S.ActionDark, S.ActionNote, () => SetDark(true)));
                break;
            case SystemActionKind.LightTheme when HasGnomeTheme:
                rows.Add(new ActionRow("☀️", S.ActionLight, S.ActionNote, () => SetDark(false)));
                break;
            case SystemActionKind.WifiOff when Tools.Find("nmcli") != null:
                rows.Add(new ActionRow("📶", S.ActionWifiOff, S.ActionNote, () => Run("nmcli", "radio", "wifi", "off")));
                break;
            case SystemActionKind.WifiOn when Tools.Find("nmcli") != null:
                rows.Add(new ActionRow("📶", S.ActionWifiOn, S.ActionNote, () => Run("nmcli", "radio", "wifi", "on")));
                break;
            case SystemActionKind.Lock when Tools.Find("loginctl") != null || Tools.Find("gdbus") != null:
                rows.Add(new ActionRow("🔒", S.ActionLock, S.ActionNote, LockScreen));
                break;
            case SystemActionKind.SwitchWindow:
                foreach (var (id, title) in OpenWindows().Where(w => SystemCommands.TitleMatches(w.Title, command.Window)).Take(5))
                    rows.Add(new ActionRow("🪟", title, S.ActionWindow, () => Run("wmctrl", "-i", "-a", id)));
                break;
        }
        return rows;
    }

    private static bool HasAudio => Tools.Find("wpctl") != null || Tools.Find("pactl") != null;

    /// GNOME 42 and later (and desktops built on it) keep the light or dark choice in color-scheme.
    private static bool HasGnomeTheme => GnomeTheme.Value;

    private static readonly Lazy<bool> GnomeTheme = new(() =>
        Output("gsettings", "writable", "org.gnome.desktop.interface", "color-scheme")?.Trim() == "true");

    // MARK: audio: PipeWire's wpctl, else PulseAudio's pactl (pipewire-pulse answers it too)

    private static void SetVolume(int level)
    {
        if (Run("wpctl", "set-volume", "@DEFAULT_AUDIO_SINK@", level + "%") && Run("wpctl", "set-mute", "@DEFAULT_AUDIO_SINK@", "0")) return;
        if (Run("pactl", "set-sink-volume", "@DEFAULT_SINK@", level + "%")) Run("pactl", "set-sink-mute", "@DEFAULT_SINK@", "0");
    }

    private static void SetMute(bool mute)
    {
        if (!Run("wpctl", "set-mute", "@DEFAULT_AUDIO_SINK@", mute ? "1" : "0"))
            Run("pactl", "set-sink-mute", "@DEFAULT_SINK@", mute ? "1" : "0");
    }

    // MARK: theme: GNOME's colour scheme, and Ubuntu's Yaru theme pair the way its Appearance settings switch it

    private static void SetDark(bool dark)
    {
        Run("gsettings", "set", "org.gnome.desktop.interface", "color-scheme", dark ? "prefer-dark" : "default");
        var theme = Output("gsettings", "get", "org.gnome.desktop.interface", "gtk-theme")?.Trim().Trim('\'') ?? "";
        if (!theme.StartsWith("Yaru", StringComparison.Ordinal)) return;
        var light = theme.EndsWith("-dark", StringComparison.Ordinal) ? theme[..^5] : theme;
        var wanted = dark ? light + "-dark" : light;
        if (wanted != theme) Run("gsettings", "set", "org.gnome.desktop.interface", "gtk-theme", wanted);
    }

    // MARK: lock: logind's session lock, else GNOME's screensaver over D-Bus

    private static void LockScreen()
    {
        if (Run("loginctl", "lock-session")) return;
        Run("gdbus", "call", "--session", "--dest", "org.gnome.ScreenSaver", "--object-path", "/org/gnome/ScreenSaver",
            "--method", "org.gnome.ScreenSaver.Lock");
    }

    // MARK: windows

    /// Open windows by title, through wmctrl on X11. Wayland gives other apps no safe way to list or raise
    /// windows (GNOME closed Shell.Eval), so there the list stays empty and no row shows.
    private static List<(string Id, string Title)> OpenWindows()
    {
        var list = new List<(string, string)>();
        if (!IsX11 || Tools.Find("wmctrl") == null) return list;
        if (DateTime.Now - _windowsAt < TimeSpan.FromSeconds(2)) return _windows;
        var output = Output("wmctrl", "-l") ?? "";
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // 0x03a00003  0 host Title with spaces
            var parts = line.Split((char[]?)null, 4, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4 || parts[1] == "-1") continue; // -1: docks and desktop
            var title = parts[3].Trim();
            if (title.Length == 0 || title == "Lumi") continue;
            list.Add((parts[0], title));
        }
        _windows = list;
        _windowsAt = DateTime.Now;
        return list;
    }

    private static List<(string Id, string Title)> _windows = new();
    private static DateTime _windowsAt = DateTime.MinValue;

    public static bool IsX11 =>
        Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") != "wayland" &&
        Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is not { Length: > 0 } &&
        Environment.GetEnvironmentVariable("DISPLAY") is { Length: > 0 };

    // MARK: running tools: no shell, fixed arguments

    /// Runs a tool and waits briefly; true when it exits with 0.
    private static bool Run(string tool, params string[] args)
    {
        if (Tools.Find(tool) is not { } path) return false;
        try
        {
            var psi = new ProcessStartInfo(path) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            if (!p.WaitForExit(5000)) return false;
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string? Output(string tool, params string[] args)
    {
        if (Tools.Find(tool) is not { } path) return null;
        try
        {
            var psi = new ProcessStartInfo(path) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(2000)) return null;
            return p.ExitCode == 0 ? output.Result : null;
        }
        catch
        {
            return null;
        }
    }

    /// Runs an action off the UI thread: the bar closes at once and the tools take their time.
    public static void Start(ActionRow row) => Task.Run(() =>
    {
        try { row.Run(); }
        catch { /* a failed tool leaves things as they were */ }
    });
}
