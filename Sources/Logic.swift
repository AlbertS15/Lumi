// Rules with no UI, kept apart from main.swift so tests/LumiCoreTests can check them (swift test).

import Foundation

enum Logic {
    /// A question selects "Ask <model>"; anything else lands on the first app or file.
    static func looksLikeQuestion(_ text: String) -> Bool {
        let t = text.trimmingCharacters(in: .whitespaces).lowercased()
        if t.hasSuffix("?") { return true }
        if t.split(separator: " ").count >= 4 { return true }
        let first = String(t.split(separator: " ").first ?? "")
        return Lang.questionWords.contains(first)
    }

    /// System and app-internal files Spotlight keeps out of its own results.
    static func isNoise(_ path: String) -> Bool {
        if path.hasSuffix(".app") && (path.hasPrefix("/Applications/") || path.hasPrefix("/System/Applications/")) {
            return false
        }
        let blocked = ["/Library/", "/usr/", "/System/", "/private/", "/opt/", "/.Trash/", "/node_modules/", ".app/Contents/"]
        if blocked.contains(where: { path.contains($0) }) { return true }
        return path.split(separator: "/").contains { $0.hasPrefix(".") }
    }

    /// Compares dotted version numbers: 1.10.0 is newer than 1.9.2.
    static func isNewer(_ a: String, than b: String) -> Bool {
        let x = a.split(separator: ".").map { Int($0) ?? 0 }
        let y = b.split(separator: ".").map { Int($0) ?? 0 }
        for i in 0..<max(x.count, y.count) {
            let l = i < x.count ? x[i] : 0, r = i < y.count ? y[i] : 0
            if l != r { return l > r }
        }
        return false
    }
}
