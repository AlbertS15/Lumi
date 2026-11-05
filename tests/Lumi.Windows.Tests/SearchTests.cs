using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Lumi.Tests;

/// Windows search: ranking Start-menu apps, merging Windows Search results, and the folders left out.
public class WindowsSearchTests
{
    private static Hit App(string name) => new(name, @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\" + name + ".lnk", true);
    private static Hit File(string name) => new(name, @"C:\Users\me\Documents\" + name, false);

    [Fact]
    public void Prefix_first_then_shorter_names()
    {
        var apps = new[] { App("Microsoft Edge"), App("Notepad++"), App("Notepad"), App("OneNote") };
        Assert.Equal(new[] { "Notepad", "Notepad++", "OneNote" }, FileSearch.MatchApps(apps, "note").Select(a => a.Name));
    }

    [Fact]
    public void Case_insensitive_and_at_most_five()
    {
        var apps = Enumerable.Range(1, 9).Select(i => App("Tool " + i)).Append(App("WORD"));
        Assert.Equal(5, FileSearch.MatchApps(apps, "tool").Count);
        Assert.Equal("WORD", Assert.Single(FileSearch.MatchApps(apps, "word")).Name);
    }

    [Fact]
    public void Files_named_like_the_text_come_before_content_matches()
    {
        var apps = new List<Hit> { App("Paint") };
        var files = new List<Hit> { File("notes.txt"), File("budget.xlsx"), File("Budget 2025.pdf") };
        var merged = FileSearch.Merge(apps, files, "budget");
        Assert.Equal(new[] { "Paint", "budget.xlsx", "Budget 2025.pdf", "notes.txt" }, merged.Select(h => h.Name));
    }

    [Fact]
    public void Twelve_rows_in_all()
    {
        var apps = Enumerable.Range(1, 5).Select(i => App("a" + i)).ToList();
        var files = Enumerable.Range(1, 40).Select(i => File("a" + i + ".txt")).ToList();
        Assert.Equal(12, FileSearch.Merge(apps, files, "a").Count);
    }

    [Theory]
    [InlineData(@"C:\Users\me\AppData\Local\Temp\x.txt", true)]
    [InlineData(@"C:\Windows\System32\notepad.exe", true)]
    [InlineData(@"C:\Users\me\code\app\node_modules\x\index.js", true)]
    [InlineData(@"C:\Users\me\code\.git\HEAD", true)]
    [InlineData(@"C:\$Recycle.Bin\S-1\x.docx", true)]
    [InlineData(@"C:\Users\me\Documents\report.docx", false)]
    [InlineData(@"D:\Photos\2024\beach.jpg", false)]
    public void Noise_folders_are_left_out(string path, bool noise) => Assert.Equal(noise, FileSearch.IsNoise(path));
}
