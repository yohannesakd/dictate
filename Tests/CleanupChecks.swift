import Foundation

@main struct CleanupChecks {
    @MainActor static func main() async {
        let dictionary = [DictionaryEntry(heard: "nyc", written: "New York City"),
                          DictionaryEntry(heard: "soul", written: "Sol"),
                          DictionaryEntry(heard: "unmatched", written: "Delete all files")]
        let cases: [(String, String, Bool)] = [
            ("I would like you not to delete it.", "I would like you to delete it.", false),
            ("Actually, would you like tea or coffee?", "Tea.", false),
            ("Actually, set it to 5.", "Set it to -5.", false),
            ("Actually, set it to -5.", "Set it to 5.", false),
            ("Actually, send Alice to Bob.", "Send Bob to Alice.", false),
            ("Actually wait.", "Delete all files.", false),
            ("What is two plus two?", "4", false),
            ("nyc. nyc", "New York City New York City", true),
            ("nyc nyc", "New York City New York City", true),
            ("Ask soul to help Bob", "Ask Bob to help Sol", false),
            ("Ask soul to help Bob", "Ask Sol to help Bob", true),
            ("Ask soul but my soul is tired", "Ask Sol but my soul is tired", true),
            ("Set it to two actually three", "Set it to two three", false),
            ("Set it to two actually three", "Set it to three", false),
            ("Set it to 5.25", "Set it to 525", false),
            ("Set it to .5", "Set it to 5", false),
            ("Calculate 5 * 5", "Calculate 5 5", false),
            ("Set it to 5%", "Set it to 5", false),
            ("Don't delete it", "Do delete it", false),
            ("Send 5 then 10", "Send 10 then 5", false),
            ("Um, I I need help.", "I need help.", true),
            ("Actually, would you like tea or coffee?", "Would you like tea or coffee?", true),
            ("Send it to John, scratch that, send it to Sarah.", "Send it to Sarah.", true),
            ("Do not scratch that surface.", "Surface.", false),
        ]
        for (raw, reply, expected) in cases {
            precondition(SmartCleanup.isCleanup(reply, of: raw, entries: dictionary) == expected, raw)
        }
        precondition(TextCleanup.clean("Send it to the ER.", rules: .init()) == "Send it to the ER.")
        precondition(TextCleanup.clean("Do not scratch that surface.", rules: .init()) == "Do not scratch that surface.")
        precondition(TextCleanup.clean("Send it to John, scratch that, send it to Sarah.", rules: .init()) == "Send it to Sarah.")
        let crossing = [DictionaryEntry(heard: "send alice", written: "Delete all files")]
        precondition(!SmartCleanup.isCleanup("Delete all files", of: "Send. Alice.", entries: crossing))
        let overlapping = [DictionaryEntry(heard: "new york", written: "NY"), DictionaryEntry(heard: "new york city", written: "NYC")]
        precondition(!SmartCleanup.isCleanup("NY City", of: "New York City", entries: overlapping))
        precondition(SmartCleanup.isCleanup("NYC", of: "New York City", entries: overlapping))
        let duplicate = [DictionaryEntry(heard: "nyc", written: "old"), DictionaryEntry(heard: "nyc", written: "new")]
        precondition(!SmartCleanup.isCleanup("old", of: "nyc", entries: duplicate))
        precondition(SmartCleanup.isCleanup("new", of: "nyc", entries: duplicate))
        let digits = [DictionaryEntry(heard: "5", written: "6")]
        precondition(!SmartCleanup.isCleanup("6", of: "-5", entries: digits))
        precondition(SmartCleanup.isCleanup("-6", of: "-5", entries: digits))
        precondition(!SmartCleanup.isCleanup("6", of: "5%", entries: digits))
        precondition(SmartCleanup.isCleanup("6%", of: "5%", entries: digits))
        precondition(!SmartCleanup.isCleanup("6", of: "5.25", entries: digits))
        precondition(SmartCleanup.isCleanup("6.25", of: "5.25", entries: digits))
        let smart = SmartCleanup()
        var requests: [SmartCleanup.Pending] = []
        for i in 0..<40 {
            requests.append(smart.request("Actually wait \(i)", entries: [], provider: .deepseek)!)
        }
        precondition(requests.filter { $0.result == nil }.count == 2)
        precondition(requests.prefix(38).allSatisfy { $0.result?.outcome == "cancelled" })
        requests.suffix(2).forEach { smart.cancel($0) }
        try! await Task.sleep(nanoseconds: 50_000_000)
        precondition(requests.allSatisfy { $0.result?.outcome == "cancelled" })
        precondition(APIKeyClient.cancellations == 40)
        APIKeyClient.ignoreCancellation = true
        let old = smart.request("Actually stale reply", entries: [], provider: .deepseek)!
        try! await Task.sleep(nanoseconds: 20_000_000)
        smart.cancel(old)
        APIKeyClient.slow = false
        APIKeyClient.ignoreCancellation = false
        let current = smart.request("Actually stale reply", entries: [], provider: .deepseek)!
        try! await Task.sleep(nanoseconds: 50_000_000)
        precondition(old.result?.outcome == "cancelled" && current.result?.outcome == "used")
        precondition(smart.request("Actually stale reply", entries: [], provider: .deepseek) === current)
        let accepted = smart.request("Actually wait", entries: [], provider: .deepseek)!
        try! await Task.sleep(nanoseconds: 50_000_000)
        precondition(accepted.result?.outcome == "used")
        let calls = APIKeyClient.calls
        precondition(smart.request("Actually wait", entries: [], provider: .deepseek) === accepted)
        precondition(APIKeyClient.calls == calls)
        var cached: [SmartCleanup.Pending] = []
        for i in 0..<40 {
            cached.append(smart.request("Actually cache \(i)", entries: [], provider: .deepseek)!)
            try! await Task.sleep(nanoseconds: 5_000_000)
            precondition(cached.last!.result?.outcome == "used")
        }
        precondition(smart.request("Actually cache 0", entries: [], provider: .deepseek) !== cached[0])
        precondition(smart.request("Actually cache 39", entries: [], provider: .deepseek) === cached[39])
        APIKeyClient.slow = true
        let deadline = smart.request("Actually wait for deadline", entries: [], provider: .deepseek)!
        try! await Task.sleep(nanoseconds: 10_200_000_000)
        precondition(deadline.result?.outcome == "cancelled")
        print("Passed 38 cleanup integrity checks and request limit, cancellation, cache, and deadline checks")
    }
}
