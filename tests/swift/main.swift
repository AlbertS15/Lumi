// Checks Sources/SystemCommands.swift against tests/system-commands.json. Run tests/swift/run.sh.
import Foundation

let path = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "tests/system-commands.json"
let data = try! Data(contentsOf: URL(fileURLWithPath: path))
let json = try! JSONSerialization.jsonObject(with: data) as! [String: Any]
var failures = 0
var count = 0

for c in json["cases"] as! [[String: Any]] {
    count += 1
    let text = c["text"] as! String
    let kind = c["kind"] as? String
    let got = SystemCommands.parse(text)
    var ok = got?.kind.rawValue == kind
    if ok, let v = c["volume"] as? Int { ok = got?.volume == v }
    if ok, let w = c["window"] as? String { ok = got?.window == w }
    if !ok {
        failures += 1
        print("FAIL \"\(text)\": expected \(kind ?? "nil") \(c["volume"] ?? "") \(c["window"] ?? ""), got \(String(describing: got))")
    }
}

for c in json["titles"] as! [[String: Any]] {
    count += 1
    let title = c["title"] as! String, query = c["query"] as! String, expected = c["matches"] as! Bool
    if SystemCommands.titleMatches(title, query) != expected {
        failures += 1
        print("FAIL title \"\(title)\" / \"\(query)\": expected \(expected)")
    }
}

print("\(count - failures)/\(count) passed")
exit(failures == 0 ? 0 : 1)
