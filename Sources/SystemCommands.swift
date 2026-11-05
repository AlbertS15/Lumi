import Foundation

/// What a short command in the bar asks the system to do.
enum SystemActionKind: String {
    case volume = "Volume"
    case mute = "Mute"
    case unmute = "Unmute"
    case darkTheme = "DarkTheme"
    case lightTheme = "LightTheme"
    case wifiOff = "WifiOff"
    case wifiOn = "WifiOn"
    case lock = "Lock"
    case switchWindow = "SwitchWindow"
}

/// A parsed command: the kind, the volume for .volume (0–100), the title to look for for .switchWindow.
struct SystemCommand: Equatable {
    let kind: SystemActionKind
    var volume = 0
    var window = ""
}

/// Turns the bar's text into a system command, in English or Russian, or nil when it isn't one.
/// Only whole phrases match, so ordinary searches and questions are left alone.
/// The same rules as windows/Lumi/SystemCommands.cs; both are checked against tests/system-commands.json.
enum SystemCommands {
    private static func regex(_ pattern: String) -> NSRegularExpression {
        // The patterns are constants, so a failure here is a programming error caught by the tests.
        try! NSRegularExpression(pattern: pattern)
    }

    private static let volume = regex(
        #"^(?:(?:set|change)\s+)?(?:volume|vol|sound)(?:\s+(?:to|at))?\s+(\d{1,3})\s*%?$"# +
        #"|^(?:(?:сделай|сделать|поставь|поставить|установи|установить|выставь|выставить)\s+)?(?:громкость|звук)(?:\s+(?:на|в))?\s+(\d{1,3})\s*%?$"#)

    private static let mute = regex(
        #"^(?:mute(?:\s+(?:sound|audio|volume))?|(?:sound|audio)\s+off|turn\s+(?:off\s+(?:the\s+)?(?:sound|audio)|(?:the\s+)?(?:sound|audio)\s+off))$"# +
        #"|^(?:без\s+звука|(?:вы|от)ключи(?:ть)?\s+звук|звук\s+(?:выкл|выключить|отключить))$"#)

    private static let unmute = regex(
        #"^(?:unmute(?:\s+(?:sound|audio|volume))?|(?:sound|audio)\s+on|turn\s+(?:on\s+(?:the\s+)?(?:sound|audio)|(?:the\s+)?(?:sound|audio)\s+on))$"# +
        #"|^(?:со\s+звуком|включи(?:ть)?\s+звук|звук\s+(?:вкл|включить))$"#)

    // "dark theme", "switch to dark mode", "тёмная тема", "включи тёмную тему"; turning one off means the other.
    private static let enOn = #"(?:(?:switch\s+to|turn\s+on|enable|use|set|go)\s+)?"#
    private static let enOff = #"(?:turn\s+off|disable)\s+"#
    private static let ruOn = #"(?:(?:включи|включить|сделай|сделать|поставь|поставить|переключи\s+на|переключись\s+на|перейди\s+на)\s+)?"#
    private static let ruOff = #"(?:выключи|выключить|отключи|отключить)\s+"#
    private static let enNoun = #"(?:theme|mode|appearance)"#
    private static let ruNoun = #"(?:тема|тему|режим|оформление)"#

    private static func theme(_ en: String, _ ru: String) -> NSRegularExpression {
        regex("^\(enOn)\(en)\\s+\(enNoun)(?:\\s+on)?$|^\(enNoun)\\s+\(en)$|^\(ruOn)\(ru)\\s+\(ruNoun)$|^\(ruNoun)\\s+\(ru)$")
    }

    private static func themeOff(_ en: String, _ ru: String) -> NSRegularExpression {
        regex("^\(enOff)\(en)\\s+\(enNoun)$|^\(en)\\s+\(enNoun)\\s+off$|^\(ruOff)\(ru)\\s+\(ruNoun)$")
    }

    private static let enDark = "dark"
    private static let ruDark = "темн(?:ая|ую|ый|ое|ой)"
    private static let enLight = "light"
    private static let ruLight = "светл(?:ая|ую|ый|ое|ой)"

    private static let dark = theme(enDark, ruDark)
    private static let light = theme(enLight, ruLight)
    private static let darkOff = themeOff(enDark, ruDark)
    private static let lightOff = themeOff(enLight, ruLight)

    private static let wifiOn = regex(
        #"^(?:wifi\s+on|(?:turn|switch)\s+on\s+(?:the\s+)?wifi|(?:turn|switch)\s+(?:the\s+)?wifi\s+on|enable\s+wifi)$"# +
        #"|^(?:включи(?:ть)?\s+wifi|wifi\s+(?:вкл|включить))$"#)

    private static let wifiOff = regex(
        #"^(?:wifi\s+off|(?:turn|switch)\s+off\s+(?:the\s+)?wifi|(?:turn|switch)\s+(?:the\s+)?wifi\s+off|disable\s+wifi)$"# +
        #"|^(?:(?:вы|от)ключи(?:ть)?\s+wifi|wifi\s+(?:выкл|выключить|отключить))$"#)

    private static let lock = regex(
        #"^(?:lock(?:\s+(?:the\s+)?(?:screen|computer|pc|session))?|lock\s+my\s+(?:screen|computer|pc))$"# +
        #"|^(?:блокировка(?:\s+экрана)?|заблокир(?:уй|овать)(?:\s+(?:экран|компьютер))?)$"#)

    private static let window = regex(
        #"^(?:switch\s+to|window|переключись\s+на|переключиться\s+на|окно)\s+(?:(?:the\s+)?window\s+|окно\s+)?(.+)$"#)

    private static let spaces = regex(#"\s+"#)
    private static let wifiWord = regex(#"(?<![\p{L}\d])(?:wi[\s-]?fi|вай[\s-]?фай)(?![\p{L}\d])"#)

    /// Lower case, ё as е, "wi-fi"/"вай-фай" as "wifi", single spaces, no closing punctuation.
    static func normalize(_ text: String) -> String {
        var t = text.lowercased().replacingOccurrences(of: "ё", with: "е")
        t = wifiWord.stringByReplacingMatches(in: t, range: NSRange(t.startIndex..., in: t), withTemplate: "wifi")
        t = spaces.stringByReplacingMatches(in: t, range: NSRange(t.startIndex..., in: t), withTemplate: " ")
        t = t.trimmingCharacters(in: .whitespaces)
        while let last = t.last, ".! ".contains(last) { t.removeLast() }
        return t
    }

    private static func match(_ r: NSRegularExpression, _ t: String) -> NSTextCheckingResult? {
        r.firstMatch(in: t, range: NSRange(t.startIndex..., in: t))
    }

    private static func group(_ m: NSTextCheckingResult, _ i: Int, in t: String) -> String? {
        guard m.range(at: i).location != NSNotFound, let r = Range(m.range(at: i), in: t) else { return nil }
        return String(t[r])
    }

    static func parse(_ text: String) -> SystemCommand? {
        let t = normalize(text)
        if t.isEmpty { return nil }

        if let m = match(volume, t) {
            guard let digits = group(m, 1, in: t) ?? group(m, 2, in: t), let level = Int(digits), level <= 100 else { return nil }
            return SystemCommand(kind: .volume, volume: level)
        }
        if match(mute, t) != nil { return SystemCommand(kind: .mute) }
        if match(unmute, t) != nil { return SystemCommand(kind: .unmute) }
        if match(dark, t) != nil || match(lightOff, t) != nil { return SystemCommand(kind: .darkTheme) }
        if match(light, t) != nil || match(darkOff, t) != nil { return SystemCommand(kind: .lightTheme) }
        if match(wifiOff, t) != nil { return SystemCommand(kind: .wifiOff) }
        if match(wifiOn, t) != nil { return SystemCommand(kind: .wifiOn) }
        if match(lock, t) != nil { return SystemCommand(kind: .lock) }

        if let m = match(window, t), let title = group(m, 1, in: t)?.trimmingCharacters(in: .whitespaces), !title.isEmpty {
            return SystemCommand(kind: .switchWindow, window: title)
        }
        return nil
    }

    /// Whether a window's title fits what was typed after "switch to" (already normalized).
    static func titleMatches(_ title: String, _ query: String) -> Bool {
        !query.isEmpty && normalize(title).contains(query)
    }
}
