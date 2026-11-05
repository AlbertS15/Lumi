using System.IO;
using System.Linq;
using Xunit;

namespace Lumi.Tests;

/// Linux search: reading .desktop files, matching and ranking apps, and which locate results are shown.
public class SearchTests
{
    private static string Desktop(string folder, string id, string body)
    {
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, id);
        File.WriteAllText(file, body);
        return file;
    }

    private static Hit App(string name) => new(name, "/usr/share/applications/" + name + ".desktop", true, name.ToLowerInvariant());

    [Fact]
    public void Reads_name_exec_and_terminal()
    {
        var file = Desktop(TestEnv.TempDir(), "htop.desktop",
            "[Desktop Entry]\nType=Application\nName=Htop\nExec=htop %F\nTerminal=true\n");
        var hit = SearchTestsHelpers.Entry(file, "en");
        Assert.Equal("Htop", hit!.Name);
        Assert.Equal("htop %F", hit.Exec);
        Assert.True(hit.InTerminal);
        Assert.True(hit.IsApp);
    }

    [Fact]
    public void Prefers_the_name_in_the_interface_language()
    {
        var file = Desktop(TestEnv.TempDir(), "files.desktop",
            "[Desktop Entry]\nType=Application\nName=Files\nName[ru]=Файлы\nName[de]=Dateien\nExec=nautilus\n");
        Assert.Equal("Файлы", SearchTestsHelpers.Entry(file, "ru")!.Name);
        Assert.Equal("Dateien", SearchTestsHelpers.Entry(file, "de")!.Name);
        Assert.Equal("Files", SearchTestsHelpers.Entry(file, "fr")!.Name);
    }

    [Fact]
    public void Ignores_keys_from_action_sections()
    {
        var file = Desktop(TestEnv.TempDir(), "fx.desktop",
            "[Desktop Entry]\nType=Application\nName=Firefox\nExec=firefox %u\n\n[Desktop Action new-window]\nName=New Window\nExec=firefox --new-window\n");
        var hit = SearchTestsHelpers.Entry(file, "en")!;
        Assert.Equal("Firefox", hit.Name);
        Assert.Equal("firefox %u", hit.Exec);
    }

    [Theory]
    [InlineData("[Desktop Entry]\nType=Application\nName=X\nExec=x\nNoDisplay=true\n")]
    [InlineData("[Desktop Entry]\nType=Application\nName=X\nExec=x\nHidden=true\n")]
    [InlineData("[Desktop Entry]\nType=Link\nName=X\nURL=https://example.com\n")]
    [InlineData("[Desktop Entry]\nType=Application\nName=X\n")]
    [InlineData("[Desktop Entry]\nType=Application\nExec=x\n")]
    [InlineData("[Other]\nName=X\nExec=x\n")]
    [InlineData("")]
    public void Skips_entries_not_shown_in_menus(string body)
    {
        var file = Desktop(TestEnv.TempDir(), "x.desktop", body);
        Assert.Null(SearchTestsHelpers.Entry(file, "en"));
    }

    [Fact]
    public void Missing_file_is_skipped() => Assert.Null(SearchTestsHelpers.Entry("/nonexistent/x.desktop", "en"));

    [Fact]
    public void The_first_folder_wins_like_the_app_menu()
    {
        var root = TestEnv.TempDir();
        var user = Path.Combine(root, "user");
        var system = Path.Combine(root, "system");
        Desktop(system, "editor.desktop", "[Desktop Entry]\nType=Application\nName=System Editor\nExec=ed\n");
        Desktop(user, "editor.desktop", "[Desktop Entry]\nType=Application\nName=My Editor\nExec=ed\n");
        // A user's override that hides a system app hides it.
        Desktop(system, "ads.desktop", "[Desktop Entry]\nType=Application\nName=Ads\nExec=ads\n");
        Desktop(user, "ads.desktop", "[Desktop Entry]\nType=Application\nName=Ads\nExec=ads\nHidden=true\n");
        Desktop(Path.Combine(system, "kde"), "nested.desktop", "[Desktop Entry]\nType=Application\nName=Nested\nExec=n\n");

        var apps = FileSearch.LoadApps(new[] { user, system, Path.Combine(root, "missing") }, "en");
        Assert.Equal(new[] { "My Editor", "Nested" }, apps.Select(a => a.Name));
    }

    [Fact]
    public void Matches_anywhere_in_the_name_ignoring_case()
    {
        var apps = new[] { App("Firefox"), App("Files"), App("LibreOffice Writer"), App("GNOME Terminal") };
        Assert.Equal(new[] { "LibreOffice Writer" }, FileSearch.MatchApps(apps, "writ").Select(a => a.Name));
        Assert.Equal(new[] { "GNOME Terminal" }, FileSearch.MatchApps(apps, "TERM").Select(a => a.Name));
        Assert.Empty(FileSearch.MatchApps(apps, "chrome"));
    }

    [Fact]
    public void Names_starting_with_the_text_come_first()
    {
        var apps = new[] { App("Notes"), App("Text Editor"), App("Calculator"), App("Terminal") };
        var hits = FileSearch.MatchApps(apps, "te").Select(a => a.Name).ToArray();
        Assert.Equal(new[] { "Text Editor", "Terminal", "Notes" }, hits);
    }

    [Fact]
    public void At_most_six_apps()
    {
        var apps = Enumerable.Range(1, 20).Select(i => App("App " + i));
        Assert.Equal(6, FileSearch.MatchApps(apps, "app").Count);
    }

    [Fact]
    public void Locate_results_are_files_in_home_only()
    {
        var home = "/home/me";
        var lines = new[]
        {
            "/home/me/Documents/report.pdf",
            "/etc/report.conf",
            "/home/me/.cache/report.tmp",
            "/home/me/.config/report.json",
            "/home/me/code/app/node_modules/report/index.js",
            "/home/me/code/.git/report",
            "/home/meg/report.txt",
            "/home/me/Desktop/report draft.odt",
        };
        var hits = FileSearch.FilterLocate(lines, home);
        Assert.Equal(new[] { "/home/me/Documents/report.pdf", "/home/me/Desktop/report draft.odt" }, hits.Select(h => h.Path));
        Assert.Equal(new[] { "report.pdf", "report draft.odt" }, hits.Select(h => h.Name));
        Assert.All(hits, h => Assert.False(h.IsApp));
    }

    [Fact]
    public void At_most_eight_files()
    {
        var lines = Enumerable.Range(1, 30).Select(i => $"/home/me/f{i}.txt");
        Assert.Equal(8, FileSearch.FilterLocate(lines, "/home/me").Count);
    }
}

internal static class SearchTestsHelpers
{
    public static Hit? Entry(string file, string lang) => FileSearch.ReadEntry(file, lang);
}
