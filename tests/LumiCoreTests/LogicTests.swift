import XCTest
@testable import LumiCore

/// The Mac app's question/search rule, Spotlight noise filter, update check and strings.
final class LogicTests: XCTestCase {
    func testQuestionsGoToTheModel() {
        for text in ["inch in cm?", "firefox?", "best pizza near me", "what is rust", "How tall", "что такое DNS",
                     "сколько времени", "wie spät", "cómo estás", "pourquoi", "où est"] {
            XCTAssertTrue(Logic.looksLikeQuestion(text), text)
        }
    }

    func testSearchesStaySearches() {
        for text in ["", "   ", "safari", "Terminal", "visual studio code", "whatsapp", "report 2024.pdf", "настройки"] {
            XCTAssertFalse(Logic.looksLikeQuestion(text), text)
        }
    }

    func testNoise() {
        XCTAssertFalse(Logic.isNoise("/Applications/Safari.app"))
        XCTAssertFalse(Logic.isNoise("/System/Applications/Calculator.app"))
        XCTAssertFalse(Logic.isNoise("/Users/me/Documents/report.pdf"))
        XCTAssertTrue(Logic.isNoise("/Users/me/Library/Caches/x.db"))
        XCTAssertTrue(Logic.isNoise("/Applications/Xcode.app/Contents/Info.plist"))
        XCTAssertTrue(Logic.isNoise("/Users/me/.config/x.json"))
        XCTAssertTrue(Logic.isNoise("/Users/me/code/node_modules/x/index.js"))
        XCTAssertTrue(Logic.isNoise("/usr/bin/swift"))
    }

    func testVersionComparison() {
        XCTAssertTrue(Logic.isNewer("1.10.0", than: "1.9.2"))
        XCTAssertTrue(Logic.isNewer("2.0.4", than: "2.0.3"))
        XCTAssertTrue(Logic.isNewer("2.1", than: "2.0.9"))
        XCTAssertFalse(Logic.isNewer("2.0.4", than: "2.0.4"))
        XCTAssertFalse(Logic.isNewer("2.0", than: "2.0.0"))
        XCTAssertFalse(Logic.isNewer("1.9.9", than: "2.0.0"))
    }

    func testFiveLanguages() {
        XCTAssertEqual(Set(Lang.codes), ["en", "ru", "de", "es", "fr"])
        XCTAssertEqual(Lang.codes.count, Lang.names.count)
    }

    /// Every accessor gives real text in every language (a missing key would fall back to the key itself).
    func testStringsInEveryLanguage() {
        let saved = UserDefaults.standard.string(forKey: "language")
        defer { UserDefaults.standard.set(saved, forKey: "language") }
        var seen = Set<String>()
        for code in Lang.codes {
            UserDefaults.standard.set(code, forKey: "language")
            let texts = [S.searchPlaceholder, S.application, S.keySpace, S.menuQuit, S.askRow("X")]
            for t in texts {
                XCTAssertFalse(t.isEmpty, code)
                XCTAssertFalse(t.contains("_"), "\(code): \(t) looks like a key")
            }
            XCTAssertTrue(S.askRow("X").contains("X"), code)
            seen.insert(S.searchPlaceholder)
        }
        XCTAssertEqual(seen.count, Lang.codes.count, "each language has its own placeholder")
    }

    func testModelsPromptNamesTheSystem() {
        XCTAssertTrue(LumiModels.system(for: "macOS").contains("macOS"))
        XCTAssertFalse(LumiModels.system(for: "macOS").contains("{os}"))
        XCTAssertEqual(LumiModels.all.map(\.memoryGB), LumiModels.all.map(\.memoryGB).sorted())
    }
}
