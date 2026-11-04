import AppKit

/// A system action shown as a row in the bar; it runs only on Enter or a click.
struct ActionRow: Equatable {
    let symbol: String
    let title: String
    let subtitle: String
    let run: () -> Void

    static func == (a: ActionRow, b: ActionRow) -> Bool { a.title == b.title && a.symbol == b.symbol }
}

/// System commands on macOS: volume through AppleScript's Standard Additions, the theme through System Events
/// (macOS asks once for permission), Wi-Fi through networksetup, and the lock screen.
/// Switching to a window by its title isn't offered: reading other apps' window titles needs the
/// Screen Recording or Accessibility permission.
enum SystemActions {
    static func rows(for text: String) -> [ActionRow] {
        guard let command = SystemCommands.parse(text) else { return [] }
        let note = S.actionNote
        switch command.kind {
        case .volume:
            let level = command.volume
            return [ActionRow(symbol: "speaker.wave.3.fill", title: S.actionVolume(String(level)), subtitle: note) {
                appleScript("set volume output volume \(level)\nset volume without output muted")
            }]
        case .mute:
            return [ActionRow(symbol: "speaker.slash.fill", title: S.actionMute, subtitle: note) {
                appleScript("set volume with output muted")
            }]
        case .unmute:
            return [ActionRow(symbol: "speaker.wave.1.fill", title: S.actionUnmute, subtitle: note) {
                appleScript("set volume without output muted")
            }]
        case .darkTheme:
            return [ActionRow(symbol: "moon.fill", title: S.actionDark, subtitle: note) { setDark(true) }]
        case .lightTheme:
            return [ActionRow(symbol: "sun.max.fill", title: S.actionLight, subtitle: note) { setDark(false) }]
        case .wifiOff:
            return [ActionRow(symbol: "wifi.slash", title: S.actionWifiOff, subtitle: note) { setWifi(false) }]
        case .wifiOn:
            return [ActionRow(symbol: "wifi", title: S.actionWifiOn, subtitle: note) { setWifi(true) }]
        case .lock:
            return [ActionRow(symbol: "lock.fill", title: S.actionLock, subtitle: note) { lockScreen() }]
        case .switchWindow:
            return []
        }
    }

    private static func appleScript(_ source: String) {
        var error: NSDictionary?
        NSAppleScript(source: source)?.executeAndReturnError(&error)
        if let error { debugLog("system action: \(error)") }
    }

    private static func setDark(_ dark: Bool) {
        appleScript("tell application \"System Events\" to tell appearance preferences to set dark mode to \(dark)")
    }

    /// The Wi-Fi interface (usually en0) from networksetup's port list, then its power.
    private static func setWifi(_ on: Bool) {
        DispatchQueue.global().async {
            let ports = output("/usr/sbin/networksetup", ["-listallhardwareports"]) ?? ""
            var device: String?
            var isWifi = false
            for line in ports.split(separator: "\n") {
                if line.hasPrefix("Hardware Port:") { isWifi = line.contains("Wi-Fi") || line.contains("AirPort") }
                if isWifi, line.hasPrefix("Device:") {
                    device = line.dropFirst("Device:".count).trimmingCharacters(in: .whitespaces)
                    break
                }
            }
            guard let device, !device.isEmpty else { return }
            _ = output("/usr/sbin/networksetup", ["-setairportpower", device, on ? "on" : "off"])
        }
    }

    /// The lock screen at once, as the menu bar's Lock Screen does; else the display sleeps, which locks
    /// when a password is required after sleep.
    private static func lockScreen() {
        typealias Lock = @convention(c) () -> Int32
        if let handle = dlopen("/System/Library/PrivateFrameworks/login.framework/Versions/Current/login", RTLD_LAZY),
           let symbol = dlsym(handle, "SACLockScreenImmediate") {
            _ = unsafeBitCast(symbol, to: Lock.self)()
            return
        }
        DispatchQueue.global().async { _ = output("/usr/bin/pmset", ["displaysleepnow"]) }
    }

    private static func output(_ tool: String, _ arguments: [String]) -> String? {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: tool)
        p.arguments = arguments
        let pipe = Pipe()
        p.standardOutput = pipe
        p.standardError = FileHandle.nullDevice
        do { try p.run() } catch { return nil }
        let data = pipe.fileHandleForReading.readDataToEndOfFile()
        p.waitUntilExit()
        return p.terminationStatus == 0 ? String(decoding: data, as: UTF8.self) : nil
    }
}
