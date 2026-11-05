using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Lumi.Tests;

/// The interface text: strings/strings.json is the source, windows/Lumi/Strings.cs (shared with Linux) and
/// Sources/Strings.swift are generated from it.
public class StringsTests
{
    private static readonly string[] Expected = { "en", "ru", "de", "es", "fr" };

    private static JsonElement Json()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestEnv.RepoRoot, "strings", "strings.json")));
        return doc.RootElement.Clone();
    }

    private static string[] JsonLanguages() => Json().GetProperty("languages").EnumerateArray().Select(l => l[0].GetString()!).ToArray();

    private static Dictionary<string, string[]> CompiledTable() =>
        (Dictionary<string, string[]>)typeof(S).GetField("Table", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

    [Fact]
    public void All_five_languages_are_there()
    {
        Assert.Equal(Expected.OrderBy(c => c), JsonLanguages().OrderBy(c => c));
        Assert.Equal(JsonLanguages(), Lang.Codes);
        Assert.Equal(Lang.Codes.Length, Lang.Names.Length);
    }

    [Fact]
    public void Every_key_has_a_text_in_every_language()
    {
        var langs = JsonLanguages();
        var problems = new List<string>();
        foreach (var entry in Json().GetProperty("strings").EnumerateObject())
        {
            var values = entry.Value.EnumerateArray().Select(v => v.GetString()).ToArray();
            if (values.Length != langs.Length) problems.Add($"{entry.Name}: {values.Length} values for {langs.Length} languages");
            for (var i = 0; i < Math.Min(values.Length, langs.Length); i++)
                if (string.IsNullOrWhiteSpace(values[i])) problems.Add($"{entry.Name}: empty in {langs[i]}");
        }
        Assert.Empty(problems);
    }

    [Fact]
    public void Placeholders_match_across_languages()
    {
        var langs = JsonLanguages();
        var problems = new List<string>();
        foreach (var entry in Json().GetProperty("strings").EnumerateObject())
        {
            var values = entry.Value.EnumerateArray().Select(v => v.GetString() ?? "").ToArray();
            var english = Placeholders(values[0]);
            for (var i = 1; i < values.Length; i++)
                if (!Placeholders(values[i]).SetEquals(english))
                    problems.Add($"{entry.Name} [{langs[i]}]: {{{string.Join(",", Placeholders(values[i]))}}} vs en {{{string.Join(",", english)}}}");
        }
        Assert.Empty(problems);
    }

    private static HashSet<string> Placeholders(string s) => Regex.Matches(s, @"\{\d\}").Select(m => m.Value).ToHashSet();

    [Fact]
    public void Every_language_has_question_words_in_lower_case()
    {
        var words = Json().GetProperty("question_words");
        foreach (var code in Expected)
        {
            Assert.True(words.TryGetProperty(code, out var list), $"no question words for {code}");
            var items = list.EnumerateArray().Select(w => w.GetString()!).ToArray();
            Assert.NotEmpty(items);
            // The bar lower-cases the typed text before looking the first word up.
            foreach (var w in items) Assert.True(w == w.ToLowerInvariant() && w.Trim() == w && !w.Contains(' '), $"question word \"{w}\" ({code}) can never match");
        }
    }

    [Fact]
    public void Compiled_table_matches_strings_json()
    {
        var table = CompiledTable();
        var json = Json().GetProperty("strings").EnumerateObject().ToDictionary(e => e.Name, e => e.Value.EnumerateArray().Select(v => v.GetString()!).ToArray());
        Assert.Equal(json.Keys.OrderBy(k => k), table.Keys.OrderBy(k => k));
        foreach (var (key, values) in table) Assert.Equal(json[key], values);
        var words = Json().GetProperty("question_words").EnumerateObject().SelectMany(l => l.Value.EnumerateArray().Select(w => w.GetString()!));
        Assert.Equal(words.ToHashSet().OrderBy(w => w), Lang.QuestionWords.OrderBy(w => w));
    }

    [Fact]
    public void Every_compiled_entry_has_all_languages()
    {
        foreach (var (key, values) in CompiledTable())
        {
            Assert.True(values.Length == Lang.Codes.Length, $"{key}: {values.Length} values");
            Assert.All(values, v => Assert.False(string.IsNullOrWhiteSpace(v), $"{key} has an empty value"));
        }
    }

    [Fact]
    public void Swift_strings_are_generated_from_the_same_json()
    {
        var swift = File.ReadAllText(Path.Combine(TestEnv.RepoRoot, "Sources", "Strings.swift"));
        foreach (var entry in Json().GetProperty("strings").EnumerateObject())
            Assert.True(swift.Contains(JsonSerializer.Serialize(entry.Name) + ": ["), $"Sources/Strings.swift lacks \"{entry.Name}\": run python3 strings/generate.py");
    }

    /// Every accessor in every language returns real text, not the key it fell back to.
    [Fact]
    public void Every_accessor_returns_text_in_every_language()
    {
        var saved = Settings.Shared.Language;
        try
        {
            var table = CompiledTable();
            var methods = typeof(S).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.ReturnType == typeof(string) && m.GetParameters().All(p => p.ParameterType == typeof(string)))
                .ToArray();
            Assert.True(methods.Length >= table.Count, $"{methods.Length} accessors for {table.Count} keys");
            foreach (var code in Lang.Codes)
            {
                Settings.Shared.Language = code;
                foreach (var m in methods)
                {
                    var args = m.GetParameters().Select((_, i) => (object)("ARG" + i)).ToArray();
                    var text = (string)m.Invoke(null, args)!;
                    Assert.False(string.IsNullOrWhiteSpace(text), $"S.{m.Name} is empty in {code}");
                    Assert.False(table.ContainsKey(text), $"S.{m.Name} fell back to its key in {code}");
                    for (var i = 0; i < args.Length; i++)
                        Assert.True(text.Contains("ARG" + i), $"S.{m.Name} drops argument {i} in {code}");
                }
            }
        }
        finally
        {
            Settings.Shared.Language = saved;
        }
    }
}
