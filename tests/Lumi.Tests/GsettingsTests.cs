using System;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace Lumi.Tests;

[CollectionDefinition("environment", DisableParallelization = true)]
public class EnvironmentCollection
{
}

/// The 2.0.4 bug end to end: Lumi's real gsettings calls against the real gsettings tool and GNOME's schema, stored in
/// a throwaway keyfile, then read back. Needs gsettings and gnome-settings-daemon's schemas (CI installs them and sets
/// LUMI_REQUIRE_GSETTINGS=1 so a missing tool fails instead of skipping).
[Collection("environment")]
public class GsettingsTests : IDisposable
{
    private const string List = "org.gnome.settings-daemon.plugins.media-keys";
    private const string Schema = List + ".custom-keybinding:/org/gnome/settings-daemon/plugins/media-keys/custom-keybindings/lumi/";

    private readonly string? _gsettings = Tools.Find("gsettings");
    private readonly string? _backend = Environment.GetEnvironmentVariable("GSETTINGS_BACKEND");

    public GsettingsTests()
    {
        // The keyfile backend writes $XDG_CONFIG_HOME/glib-2.0/settings/keyfile: nothing touches the real dconf.
        Environment.SetEnvironmentVariable("GSETTINGS_BACKEND", "keyfile");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", TestEnv.TempDir());
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("GSETTINGS_BACKEND", _backend);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", TestEnv.Config);
    }

    private void RequireGsettings()
    {
        var required = Environment.GetEnvironmentVariable("LUMI_REQUIRE_GSETTINGS") == "1";
        var ok = _gsettings != null && Gsettings("list-keys", List).Code == 0;
        if (ok) return;
        if (required) Assert.Fail("gsettings or the org.gnome.settings-daemon.plugins.media-keys schema is missing");
        Assert.Skip("gsettings with GNOME's media-keys schema is not installed");
    }

    private (int Code, string Out, string Err) Gsettings(params string[] args)
    {
        var psi = new ProcessStartInfo(_gsettings ?? "gsettings") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, stdout.Trim(), stderr.Trim());
    }

    [Theory]
    [InlineData("/usr/bin/lumi")]
    [InlineData("/home/me/My Apps/Lumi-x86_64.AppImage")]
    [InlineData("/home/o'brien/lumi")]
    [InlineData("/home/юзер/Lumi \"beta\"/lumi")]
    public void Lumi_registers_Ctrl_Space_in_GNOME(string path)
    {
        RequireGsettings();
        var command = Hotkeys.ToggleCommandFor(path);
        Hotkeys.SetGnomeShortcut(Shortcut.Standard, command, _gsettings!);

        Assert.Contains("/custom-keybindings/lumi/", Gsettings("get", List, "custom-keybindings").Out);
        Assert.Equal("'Lumi'", Gsettings("get", Schema, "name").Out);
        Assert.Equal("'<Control>space'", Gsettings("get", Schema, "binding").Out);
        var stored = Gsettings("get", Schema, "command").Out;
        Assert.NotEqual("''", stored);
        Assert.Equal(command, HotkeyTests.ParseGVariantString(stored));
    }

    [Fact]
    public void Lumi_keeps_existing_custom_shortcuts_and_registers_once()
    {
        RequireGsettings();
        var other = "/org/gnome/settings-daemon/plugins/media-keys/custom-keybindings/custom0/";
        Assert.Equal(0, Gsettings("set", List, "custom-keybindings", $"['{other}']").Code);
        Hotkeys.SetGnomeShortcut(Shortcut.Standard, "\"/usr/bin/lumi\" --toggle", _gsettings!);
        Hotkeys.SetGnomeShortcut(new Shortcut(Shortcut.Control | Shortcut.Alt, 'l'), "\"/usr/bin/lumi\" --toggle", _gsettings!);

        var list = Gsettings("get", List, "custom-keybindings").Out;
        Assert.Contains(other, list);
        Assert.Equal(list.IndexOf("/lumi/", StringComparison.Ordinal), list.LastIndexOf("/lumi/", StringComparison.Ordinal));
        Assert.Equal("'<Control><Alt>l'", Gsettings("get", Schema, "binding").Out);
    }

    [Fact]
    public void Gsettings_rejects_the_unquoted_command_of_2_0_3()
    {
        RequireGsettings();
        var result = Gsettings("set", Schema, "command", "\"/usr/bin/lumi\" --toggle");
        // If this ever passes, gsettings changed its parsing and the GVariant test above needs a second look.
        Assert.NotEqual(0, result.Code);
    }
}
