using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Lumi.Tests;

/// settings.json: what is kept across launches, the one-time move from ClaudeLight, and recovery from a broken file.
public class SettingsTests
{
    private static (string File, string Old) Paths()
    {
        var dir = TestEnv.TempDir();
        return (Path.Combine(dir, "Lumi", "settings.json"), Path.Combine(dir, "ClaudeLight", "settings.json"));
    }

    [Fact]
    public void Defaults_when_there_is_no_file()
    {
        var (file, old) = Paths();
        var s = Settings.Load(file, old);
        Assert.Equal("claude:", s.Choice);
        Assert.Empty(s.Connections);
        Assert.True(s.ShowWelcomeOnLaunch);
        Assert.True(s.ShowChatGpt);
        Assert.Equal("", s.Language);
        Assert.Equal(0u, s.HotkeyKey);
        Assert.Equal(0u, s.HotkeyModifiers);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void Save_then_load_keeps_everything()
    {
        var (file, old) = Paths();
        var s = Settings.Load(file, old);
        var c = new Connection { Name = "Groq", BaseUrl = "https://api.groq.com/openai/v1", Model = "llama-3.3-70b-versatile", Key = "sk-test-123" };
        s.Connections.Add(c);
        s.Choice = "custom:" + c.Id;
        s.ShowWelcomeOnLaunch = false;
        s.ShowChatGpt = false;
        s.Language = "de";
        s.HotkeyModifiers = 12;
        s.HotkeyKey = 0x6B;
        var changed = 0;
        s.Changed += () => changed++;
        s.SaveTo(file);
        Assert.Equal(1, changed);

        var back = Settings.Load(file, old);
        Assert.Equal(s.Choice, back.Choice);
        Assert.False(back.ShowWelcomeOnLaunch);
        Assert.False(back.ShowChatGpt);
        Assert.Equal("de", back.Language);
        Assert.Equal(12u, back.HotkeyModifiers);
        Assert.Equal(0x6Bu, back.HotkeyKey);
        var only = Assert.Single(back.Connections);
        Assert.Equal(c.Id, only.Id);
        Assert.Equal("Groq", only.Name);
        Assert.Equal(c.BaseUrl, only.BaseUrl);
        Assert.Equal(c.Model, only.Model);
        Assert.Equal("sk-test-123", only.Key);
        Assert.Same(only, back.Connection);
    }

    [Fact]
    public void The_key_is_not_stored_as_a_plain_json_field()
    {
        var (file, old) = Paths();
        var s = Settings.Load(file, old);
        s.Connections.Add(new Connection { Name = "x", Key = "sk-test-456" });
        s.SaveTo(file);
        var json = File.ReadAllText(file);
        Assert.DoesNotContain("\"Key\"", json);
        Assert.Contains("ProtectedKey", json);
        if (OperatingSystem.IsWindows()) Assert.DoesNotContain("sk-test-456", json); // DPAPI
    }

    [Fact]
    public void The_file_is_private_on_linux_and_mac()
    {
        if (OperatingSystem.IsWindows()) return;
        var (file, old) = Paths();
        Settings.Load(file, old).SaveTo(file);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
    }

    [Fact]
    public void ClaudeLight_settings_move_over_once()
    {
        var (file, old) = Paths();
        Directory.CreateDirectory(Path.GetDirectoryName(old)!);
        File.WriteAllText(old, "{\"Choice\":\"claude:opus\",\"Language\":\"ru\",\"ShowChatGpt\":false}");

        var s = Settings.Load(file, old);
        Assert.Equal("claude:opus", s.Choice);
        Assert.Equal("ru", s.Language);
        Assert.False(s.ShowChatGpt);
        Assert.True(File.Exists(file));
        Assert.True(File.Exists(old)); // copied, not moved: an older Lumi still finds its file
    }

    [Fact]
    public void ClaudeLight_settings_never_overwrite_Lumi_settings()
    {
        var (file, old) = Paths();
        Directory.CreateDirectory(Path.GetDirectoryName(old)!);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(old, "{\"Language\":\"ru\"}");
        File.WriteAllText(file, "{\"Language\":\"fr\"}");
        Assert.Equal("fr", Settings.Load(file, old).Language);
        Assert.Contains("fr", File.ReadAllText(file));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("not json")]
    [InlineData("{\"Connections\": 5}")]
    [InlineData("null")]
    public void A_broken_file_starts_over_with_defaults(string content)
    {
        var (file, old) = Paths();
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content);
        var s = Settings.Load(file, old);
        Assert.Equal("claude:", s.Choice);
        Assert.Empty(s.Connections);
    }

    [Fact]
    public void Older_files_without_new_fields_load_with_defaults()
    {
        var (file, old) = Paths();
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "{\"Choice\":\"claude:sonnet\",\"Connections\":[]}");
        var s = Settings.Load(file, old);
        Assert.Equal("claude:sonnet", s.Choice);
        Assert.True(s.ShowChatGpt);
        Assert.True(s.ShowWelcomeOnLaunch);
        Assert.Equal(0u, s.HotkeyKey);
        Assert.Equal("sonnet", s.ClaudeModel.Id);
    }

    [Fact]
    public void A_choice_of_a_removed_connection_falls_back_to_Claude()
    {
        var (file, old) = Paths();
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, $"{{\"Choice\":\"custom:{Guid.NewGuid()}\",\"Connections\":[]}}");
        Assert.Equal("claude:", Settings.Load(file, old).Choice);
    }

    [Fact]
    public void Removing_the_chosen_connection_selects_Claude()
    {
        var s = new Settings();
        var c = new Connection { Name = "a" };
        s.Connections.Add(c);
        s.Choice = "custom:" + c.Id;
        Assert.Same(c, s.Connection);
        // Remove() saves to the real path, so do what it does without the save.
        s.Connections.RemoveAll(x => x.Id == c.Id);
        Assert.Null(s.Connection);
    }

    [Theory]
    [InlineData("claude:", "")]
    [InlineData("claude:haiku", "haiku")]
    [InlineData("claude:opus", "opus")]
    [InlineData("claude:unknown", "")]
    [InlineData("gemini:", "")]
    public void Claude_model_from_choice(string choice, string id) =>
        Assert.Equal(id, new Settings { Choice = choice }.ClaudeModel.Id);

    [Theory]
    [InlineData("gemini:", "")]
    [InlineData("gemini:gemini-3.8-flash", "gemini-3.8-flash")]
    [InlineData("gemini:retired-model", "")]
    public void Gemini_model_from_choice(string choice, string id) =>
        Assert.Equal(id, new Settings { Choice = choice }.GeminiModel!.Id);

    [Fact]
    public void Not_gemini_and_not_codex_for_claude()
    {
        var s = new Settings { Choice = "claude:opus" };
        Assert.Null(s.GeminiModel);
        Assert.False(s.IsCodex);
        Assert.True(new Settings { Choice = "codex:" }.IsCodex);
    }

    [Fact]
    public void Empty_key_clears_the_stored_key()
    {
        var c = new Connection { Key = "sk-test" };
        Assert.Equal("sk-test", c.Key);
        c.Key = "";
        Assert.Equal("", c.ProtectedKey);
        Assert.Null(c.Key);
    }

    [Fact]
    public void Service_presets_are_well_formed()
    {
        Assert.Equal(ServicePreset.All.Length, ServicePreset.All.Select(p => p.Key).Distinct().Count());
        foreach (var p in ServicePreset.All.Where(p => p.Key != "other"))
            Assert.True(Uri.TryCreate(p.BaseUrl, UriKind.Absolute, out var u) && (u.Scheme == "https" || u.IsLoopback), p.Key);
        Assert.True(Ollama.IsOllama(ServicePreset.All.Single(p => p.Key == "ollama").BaseUrl));
    }
}
