using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Lumi.Tests;

/// The phrase table in tests/system-commands.json, shared with the Mac app's tests.
public class SystemCommandsTests
{
    private static JsonElement Table()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "system-commands.json");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement;
    }

    public static IEnumerable<object?[]> Cases()
    {
        foreach (var c in Table().GetProperty("cases").EnumerateArray())
        {
            var kind = c.GetProperty("kind");
            yield return new object?[]
            {
                c.GetProperty("text").GetString()!,
                kind.ValueKind == JsonValueKind.Null ? null : kind.GetString(),
                c.TryGetProperty("volume", out var v) ? v.GetInt32() : (int?)null,
                c.TryGetProperty("window", out var w) ? w.GetString() : null,
            };
        }
    }

    public static IEnumerable<object[]> Titles()
    {
        foreach (var c in Table().GetProperty("titles").EnumerateArray())
            yield return new object[] { c.GetProperty("title").GetString()!, c.GetProperty("query").GetString()!, c.GetProperty("matches").GetBoolean() };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Parses(string text, string? kind, int? volume, string? window)
    {
        var command = SystemCommands.Parse(text);
        if (kind == null)
        {
            Assert.Null(command);
            return;
        }
        Assert.NotNull(command);
        Assert.Equal(Enum.Parse<SystemActionKind>(kind), command!.Kind);
        if (volume is int level) Assert.Equal(level, command.Volume);
        if (window != null) Assert.Equal(window, command.Window);
    }

    [Theory]
    [MemberData(nameof(Titles))]
    public void MatchesTitles(string title, string query, bool matches) =>
        Assert.Equal(matches, SystemCommands.TitleMatches(title, query));

    [Fact]
    public void NormalizesSpellings()
    {
        Assert.Equal("выключи wifi", SystemCommands.Normalize("  Выключи   Вай-Фай! "));
        Assert.Equal("тема темная", SystemCommands.Normalize("Тема ТЁМНАЯ."));
        Assert.Equal("wifi off", SystemCommands.Normalize("WI FI off"));
    }

    [Fact]
    public void EveryKindHasAPhrase()
    {
        var seen = new HashSet<string>();
        foreach (var c in Cases()) if (c[1] is string k) seen.Add(k);
        foreach (var kind in Enum.GetNames<SystemActionKind>()) Assert.Contains(kind, seen);
    }
}
