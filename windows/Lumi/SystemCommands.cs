using System;
using System.Text.RegularExpressions;

namespace Lumi;

/// What a short command in the bar asks the system to do.
public enum SystemActionKind
{
    Volume,
    Mute,
    Unmute,
    DarkTheme,
    LightTheme,
    WifiOff,
    WifiOn,
    Lock,
    SwitchWindow,
}

/// A parsed command: the kind, the volume for Volume (0–100), the title to look for for SwitchWindow.
public sealed record SystemCommand(SystemActionKind Kind, int Volume = 0, string Window = "");

/// Turns the bar's text into a system command, in English or Russian, or null when it isn't one.
/// Only whole phrases match, so ordinary searches and questions are left alone.
/// Shared by the Windows and Linux apps; the Mac app has the same rules in Sources/SystemCommands.swift,
/// and both are checked against tests/system-commands.json.
public static class SystemCommands
{
    private const RegexOptions Options = RegexOptions.CultureInvariant;

    private static readonly Regex Volume = new(
        @"^(?:(?:set|change)\s+)?(?:volume|vol|sound)(?:\s+(?:to|at))?\s+(\d{1,3})\s*%?$" +
        @"|^(?:(?:сделай|сделать|поставь|поставить|установи|установить|выставь|выставить)\s+)?(?:громкость|звук)(?:\s+(?:на|в))?\s+(\d{1,3})\s*%?$",
        Options);

    private static readonly Regex Mute = new(
        @"^(?:mute(?:\s+(?:sound|audio|volume))?|(?:sound|audio)\s+off|turn\s+(?:off\s+(?:the\s+)?(?:sound|audio)|(?:the\s+)?(?:sound|audio)\s+off))$" +
        @"|^(?:без\s+звука|(?:вы|от)ключи(?:ть)?\s+звук|звук\s+(?:выкл|выключить|отключить))$",
        Options);

    private static readonly Regex Unmute = new(
        @"^(?:unmute(?:\s+(?:sound|audio|volume))?|(?:sound|audio)\s+on|turn\s+(?:on\s+(?:the\s+)?(?:sound|audio)|(?:the\s+)?(?:sound|audio)\s+on))$" +
        @"|^(?:со\s+звуком|включи(?:ть)?\s+звук|звук\s+(?:вкл|включить))$",
        Options);

    // "dark theme", "switch to dark mode", "тёмная тема", "включи тёмную тему"; turning one off means the other.
    private const string EnOn = @"(?:(?:switch\s+to|turn\s+on|enable|use|set|go)\s+)?";
    private const string EnOff = @"(?:turn\s+off|disable)\s+";
    private const string RuOn = @"(?:(?:включи|включить|сделай|сделать|поставь|поставить|переключи\s+на|переключись\s+на|перейди\s+на)\s+)?";
    private const string RuOff = @"(?:выключи|выключить|отключи|отключить)\s+";
    private const string EnNoun = @"(?:theme|mode|appearance)";
    private const string RuNoun = @"(?:тема|тему|режим|оформление)";

    private static Regex Theme(string en, string ru) => new(
        $@"^{EnOn}{en}\s+{EnNoun}(?:\s+on)?$|^{EnNoun}\s+{en}$" +
        $@"|^{RuOn}{ru}\s+{RuNoun}$|^{RuNoun}\s+{ru}$",
        Options);

    private static Regex ThemeOff(string en, string ru) => new(
        $@"^{EnOff}{en}\s+{EnNoun}$|^{en}\s+{EnNoun}\s+off$|^{RuOff}{ru}\s+{RuNoun}$",
        Options);

    private const string EnDark = "dark";
    private const string RuDark = "темн(?:ая|ую|ый|ое|ой)";
    private const string EnLight = "light";
    private const string RuLight = "светл(?:ая|ую|ый|ое|ой)";

    private static readonly Regex Dark = Theme(EnDark, RuDark);
    private static readonly Regex Light = Theme(EnLight, RuLight);
    private static readonly Regex DarkOff = ThemeOff(EnDark, RuDark);
    private static readonly Regex LightOff = ThemeOff(EnLight, RuLight);

    private static readonly Regex WifiOn = new(
        @"^(?:wifi\s+on|(?:turn|switch)\s+on\s+(?:the\s+)?wifi|(?:turn|switch)\s+(?:the\s+)?wifi\s+on|enable\s+wifi)$" +
        @"|^(?:включи(?:ть)?\s+wifi|wifi\s+(?:вкл|включить))$",
        Options);

    private static readonly Regex WifiOff = new(
        @"^(?:wifi\s+off|(?:turn|switch)\s+off\s+(?:the\s+)?wifi|(?:turn|switch)\s+(?:the\s+)?wifi\s+off|disable\s+wifi)$" +
        @"|^(?:(?:вы|от)ключи(?:ть)?\s+wifi|wifi\s+(?:выкл|выключить|отключить))$",
        Options);

    private static readonly Regex Lock = new(
        @"^(?:lock(?:\s+(?:the\s+)?(?:screen|computer|pc|session))?|lock\s+my\s+(?:screen|computer|pc))$" +
        @"|^(?:блокировка(?:\s+экрана)?|заблокир(?:уй|овать)(?:\s+(?:экран|компьютер))?)$",
        Options);

    private static readonly Regex Window = new(
        @"^(?:switch\s+to|window|переключись\s+на|переключиться\s+на|окно)\s+(?:(?:the\s+)?window\s+|окно\s+)?(.+)$",
        Options);

    private static readonly Regex Spaces = new(@"\s+", Options);
    private static readonly Regex WifiWord = new(@"(?<![\p{L}\d])(?:wi[\s-]?fi|вай[\s-]?фай)(?![\p{L}\d])", Options);

    /// Lower case, ё as е, "wi-fi"/"вай-фай" as "wifi", single spaces, no closing punctuation.
    public static string Normalize(string text)
    {
        var t = (text ?? "").ToLowerInvariant().Replace('ё', 'е');
        t = WifiWord.Replace(t, "wifi");
        t = Spaces.Replace(t, " ").Trim();
        return t.TrimEnd('.', '!', ' ');
    }

    public static SystemCommand? Parse(string text)
    {
        var t = Normalize(text);
        if (t.Length == 0) return null;

        var m = Volume.Match(t);
        if (m.Success)
        {
            var digits = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            var level = int.Parse(digits);
            return level <= 100 ? new SystemCommand(SystemActionKind.Volume, level) : null;
        }
        if (Mute.IsMatch(t)) return new SystemCommand(SystemActionKind.Mute);
        if (Unmute.IsMatch(t)) return new SystemCommand(SystemActionKind.Unmute);
        if (Dark.IsMatch(t) || LightOff.IsMatch(t)) return new SystemCommand(SystemActionKind.DarkTheme);
        if (Light.IsMatch(t) || DarkOff.IsMatch(t)) return new SystemCommand(SystemActionKind.LightTheme);
        if (WifiOff.IsMatch(t)) return new SystemCommand(SystemActionKind.WifiOff);
        if (WifiOn.IsMatch(t)) return new SystemCommand(SystemActionKind.WifiOn);
        if (Lock.IsMatch(t)) return new SystemCommand(SystemActionKind.Lock);

        m = Window.Match(t);
        if (m.Success && m.Groups[1].Value.Trim().Length > 0)
            return new SystemCommand(SystemActionKind.SwitchWindow, Window: m.Groups[1].Value.Trim());
        return null;
    }

    /// Whether a window's title fits what was typed after "switch to" (already normalized).
    public static bool TitleMatches(string title, string query) =>
        query.Length > 0 && Normalize(title).Contains(query, StringComparison.Ordinal);
}
