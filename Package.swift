// swift-tools-version:5.9
// Only for tests: `swift test` checks the Mac app's UI-free code. The app itself is built by build.sh with swiftc.
import PackageDescription

let package = Package(
    name: "Lumi",
    platforms: [.macOS(.v14)],
    targets: [
        .target(
            name: "LumiCore",
            path: "Sources",
            exclude: ["main.swift"],
            sources: ["Logic.swift", "Strings.swift", "LumiModels.swift"]
        ),
        .testTarget(name: "LumiCoreTests", dependencies: ["LumiCore"], path: "tests/LumiCoreTests"),
    ]
)
