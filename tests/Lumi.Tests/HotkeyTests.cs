using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Avalonia.Input;
using Xunit;

namespace Lumi.Tests;

/// The Linux shortcut: which mechanism is used, the GNOME notation, and the exact gsettings calls for GNOME on Wayland.
public class HotkeyTests
{
    private const string List = "org.gnome.settings-daemon.plugins.media-keys";
    private const string Path = "/org/gnome/settings-daemon/plugins/media-keys/custom-keybindings/lumi/";
    private const string Schema = List + ".custom-keybinding:" + Path;

    /// Reads a GVariant string literal the way gsettings does: single or double quotes, backslash escapes.
    /// Throws where gsettings would say "expected end of input" or similar.
    internal static string ParseGVariantString(string literal)
    {
        if (literal.Length < 2 || (literal[0] != '\'' && literal[0] != '"')) throw new FormatException("not a string literal: " + literal);
        var quote = literal[0];
        var sb = new StringBuilder();
        var i = 1;
        for (; i < literal.Length; i++)
        {
            var ch = literal[i];
            if (ch == '\\')
            {
                if (++i >= literal.Length) throw new FormatException("dangling backslash");
                sb.Append(literal[i] switch { 'n' => '\n', 't' => '\t', 'r' => '\r', var c => c });
                continue;
            }
            if (ch == quote) break;
            sb.Append(ch);
        }
        if (i != literal.Length - 1) throw new FormatException("expected end of input: " + literal);
        return sb.ToString();
    }

    [Theory]
    [InlineData("Lumi")]
    [InlineData("\"/usr/bin/lumi\" --toggle")]
    [InlineData("\"/home/me/My Apps/Lumi-x86_64.AppImage\" --toggle")]
    [InlineData("\"/home/o'brien/lumi\" --toggle")]
    [InlineData("\"/opt/it's \"quoted\"/lumi\" --toggle")]
    [InlineData("\"C:\\weird\\path\\lumi\" --toggle")]
    [InlineData("\"/home/юзер/Программы/lumi\" --toggle")]
    [InlineData("<Control>space")]
    [InlineData("")]
    [InlineData("'")]
    [InlineData("\\")]
    [InlineData("\\'")]
    public void Quote_makes_a_GVariant_string_that_reads_back_unchanged(string value)
    {
        var literal = Hotkeys.Quote(value);
        Assert.StartsWith("'", literal);
        Assert.EndsWith("'", literal);
        Assert.Equal(value, ParseGVariantString(literal));
    }

    [Fact]
    public void Unquoted_command_is_not_a_valid_GVariant_string()
    {
        // What Lumi sent before 2.0.4: gsettings refused it and Ctrl+Space did nothing.
        Assert.Throws<FormatException>(() => ParseGVariantString("\"/usr/bin/lumi\" --toggle"));
    }

    [Fact]
    public void Toggle_command_quotes_the_path()
    {
        Assert.Equal("\"/usr/bin/lumi\" --toggle", Hotkeys.ToggleCommandFor("/usr/bin/lumi"));
        Assert.Equal("\"/home/me/My Apps/Lumi.AppImage\" --toggle", Hotkeys.ToggleCommandFor("/home/me/My Apps/Lumi.AppImage"));
    }

    [Fact]
    public void First_registration_adds_Lumi_to_an_empty_list()
    {
        var cmds = Hotkeys.GnomeCommands("@as []", Shortcut.Standard, "\"/usr/bin/lumi\" --toggle");
        Assert.Equal(new[] { "set", List, "custom-keybindings", $"['{Path}']" }, cmds[0]);
        Assert.Equal(new[] { "set", Schema, "name", "'Lumi'" }, cmds[1]);
        Assert.Equal(new[] { "set", Schema, "command", "'\"/usr/bin/lumi\" --toggle'" }, cmds[2]);
        Assert.Equal(new[] { "set", Schema, "binding", "'<Control>space'" }, cmds[3]);
        Assert.Equal(4, cmds.Length);
    }

    [Fact]
    public void Registration_keeps_the_persons_other_shortcuts()
    {
        var current = "['/org/gnome/settings-daemon/plugins/media-keys/custom-keybindings/custom0/', '/x/']";
        var cmds = Hotkeys.GnomeCommands(current, Shortcut.Standard, "lumi --toggle");
        Assert.Equal(
            $"['/org/gnome/settings-daemon/plugins/media-keys/custom-keybindings/custom0/', '/x/', '{Path}']",
            cmds[0][3]);
    }

    [Fact]
    public void Registering_again_does_not_touch_the_list()
    {
        var cmds = Hotkeys.GnomeCommands($"['/a/', '{Path}']", Shortcut.Standard, "lumi --toggle");
        Assert.DoesNotContain(cmds, c => c[2] == "custom-keybindings");
        Assert.Equal(3, cmds.Length);
    }

    [Fact]
    public void Turning_the_shortcut_off_clears_the_binding()
    {
        var cmds = Hotkeys.GnomeCommands($"['{Path}']", null, "lumi --toggle");
        Assert.Equal(new[] { "set", Schema, "binding", "''" }, cmds.Last());
    }

    [Fact]
    public void Every_gsettings_value_is_valid_GVariant()
    {
        foreach (var path in new[] { "/usr/bin/lumi", "/home/a b/Lumi.AppImage", "/home/o'neil/lumi", "/tmp/\\x/lumi" })
        foreach (var key in new Shortcut?[] { Shortcut.Standard, new(Shortcut.Control | Shortcut.Alt, 'k'), null })
        foreach (var cmd in Hotkeys.GnomeCommands("@as []", key, Hotkeys.ToggleCommandFor(path)).Where(c => c[2] != "custom-keybindings"))
            ParseGVariantString(cmd[3]);
    }

    [Theory]
    [InlineData(0u, 0u, "<Control>space")]
    [InlineData(Shortcut.Control | Shortcut.Alt, 0x6Bu, "<Control><Alt>k")]
    [InlineData(Shortcut.Super, 0xFFC2u, "<Super>F5")]
    [InlineData(Shortcut.Alt, Shortcut.Grave, "<Alt>grave")]
    [InlineData(Shortcut.Control | Shortcut.Shift, Shortcut.Space, "<Control><Shift>space")]
    public void Gnome_notation(uint mods, uint key, string expected) => Assert.Equal(expected, new Shortcut(mods, key).Gnome);

    [Fact]
    public void Standard_shortcut_is_Ctrl_Space()
    {
        Assert.True(Shortcut.Standard.IsStandard);
        Assert.Equal(Shortcut.Space, Shortcut.Standard.Keysym);
        Assert.Equal(Shortcut.Control, Shortcut.Standard.Mask);
    }

    [Fact]
    public void Recording_a_shortcut()
    {
        Assert.Null(Shortcut.From(Key.K, KeyModifiers.None)); // would fire while typing
        Assert.Null(Shortcut.From(Key.K, KeyModifiers.Shift));
        Assert.Null(Shortcut.From(Key.Enter, KeyModifiers.Control)); // not a key Lumi can grab
        Assert.Equal(new Shortcut(Shortcut.Control, 'k'), Shortcut.From(Key.K, KeyModifiers.Control));
        Assert.Equal(new Shortcut(Shortcut.Alt | Shortcut.Shift, '1'), Shortcut.From(Key.D1, KeyModifiers.Alt | KeyModifiers.Shift));
        Assert.Equal(new Shortcut(0, 0xFFBE), Shortcut.From(Key.F1, KeyModifiers.None)); // function keys need no modifier
        Assert.Equal(new Shortcut(Shortcut.Super, Shortcut.Space), Shortcut.From(Key.Space, KeyModifiers.Meta));
    }

    [Theory]
    [InlineData("x11", "GNOME", ":0", true, HotkeyMode.X11)]
    [InlineData("", "KDE", ":1", false, HotkeyMode.X11)]
    [InlineData("wayland", "ubuntu:GNOME", ":0", true, HotkeyMode.Gnome)]
    [InlineData("wayland", "GNOME", "", true, HotkeyMode.Gnome)]
    [InlineData("wayland", "GNOME", "", false, HotkeyMode.Manual)]
    [InlineData("wayland", "KDE", ":0", true, HotkeyMode.Manual)]
    [InlineData("wayland", "sway", "", false, HotkeyMode.Manual)]
    [InlineData("tty", "", "", false, HotkeyMode.Manual)]
    public void Mode_detection(string session, string desktop, string display, bool gsettings, HotkeyMode expected) =>
        Assert.Equal(expected, Hotkeys.Detect(session, desktop, display, gsettings));

    /// GNOME splits the command like a shell (g_shell_parse_argv): the path must come back as one argument.
    [Theory]
    [InlineData("/usr/bin/lumi")]
    [InlineData("/home/me/My Apps/Lumi-x86_64.AppImage")]
    [InlineData("/home/o'brien/Apps/lumi")]
    [InlineData("/home/юзер/lumi")]
    public void Toggle_command_splits_into_path_and_flag(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        var psi = new ProcessStartInfo("/bin/sh") { RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var a in new[] { "-c", "eval \"set -- $1\"; printf '%s\\n' \"$@\"", "sh", Hotkeys.ToggleCommandFor(path) }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var argv = p.StandardOutput.ReadToEnd().TrimEnd('\n').Split('\n');
        p.WaitForExit();
        Assert.Equal(new[] { path, "--toggle" }, argv);
    }
}
