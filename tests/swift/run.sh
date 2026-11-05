#!/bin/zsh
# Builds and runs the Mac parser's tests: tests/swift/run.sh
set -euo pipefail
cd "${0:A:h}/../.."
out=$(mktemp -d)
swiftc -swift-version 5 Sources/SystemCommands.swift tests/swift/main.swift -o "$out/system-commands-tests"
"$out/system-commands-tests" tests/system-commands.json
