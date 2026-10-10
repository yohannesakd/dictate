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
            ("Actually, would you like tea or coffee?", "Would you like tea or coffee?", false),
            ("Send it to John, scratch that, send it to Sarah.", "Send it to Sarah.", true),
            ("Do not scratch that surface.", "Surface.", false),
            ("It looks like it'll rain, you know?", "It looks like it'll rain.", false),
            ("Is it like, is it possible to get it by Monday?", "Is it possible to get it by Monday?", true),
            ("Let's meet at noon, sorry, at one.", "Let's meet at one.", false),
            ("It went from twenty to thirty, sorry, thirty five dollars.", "It went from twenty to thirty-five dollars.", false),
            ("I'm sorry I missed the call.", "I missed the call.", false),
            ("Send it to Maria, no wait, send it to James.", "Send it to Maria.", false),
            ("Set it to two, actually, three.", "Set it to two three.", false),
            ("I do not think we should ship it, you know.", "I think we should ship it.", false),
            ("I want to, I want to go home, you know?", "I want to go home, you know?", true),
            ("We need the new, the new design.", "We need the new design.", true),
            ("The man saw the man.", "The man.", false),
            ("Is it like, is it possible?", "Is it possible?", true),
            ("Is it, like, possible?", "Is it possible?", false),
            ("I do not, I do want it.", "I do want it.", true),
            ("Had had a long day, I had had a long day.", "I had a long day.", false),
            ("So move it to Thursday, actually Friday.", "So move it to Friday, actually.", false),
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
        smart.requestsPerMinute = .max
        var requests: [SmartCleanup.Pending] = []
        for i in 0..<40 {
            requests.append(smart.request("Wait, wait \(i)", entries: [], provider: .deepseek)!)
        }
        precondition(requests.filter { $0.result == nil }.count == 2)
        precondition(requests.prefix(38).allSatisfy { $0.result?.outcome == "cancelled" })
        requests.suffix(2).forEach { smart.cancel($0) }
        try! await Task.sleep(nanoseconds: 50_000_000)
        precondition(requests.allSatisfy { $0.result?.outcome == "cancelled" })
        precondition(APIKeyClient.cancellations == 40)
        APIKeyClient.ignoreCancellation = true
        let old = smart.request("Wait, wait stale reply", entries: [], provider: .deepseek)!
        try! await Task.sleep(nanoseconds: 20_000_000)
        smart.cancel(old)
        APIKeyClient.slow = false
        APIKeyClient.ignoreCancellation = false
        let current = smart.request("Wait, wait stale reply", entries: [], provider: .deepseek)!
        try! await Task.sleep(nanoseconds: 50_000_000)
        precondition(old.result?.outcome == "cancelled" && current.result?.outcome == "used")
        precondition(smart.request("Wait, wait stale reply", entries: [], provider: .deepseek) === current)
        let accepted = smart.request("Wait, wait wait", entries: [], provider: .deepseek)!
        try! await Task.sleep(nanoseconds: 50_000_000)
        precondition(accepted.result?.outcome == "used")
        let calls = APIKeyClient.calls
        precondition(smart.request("Wait, wait wait", entries: [], provider: .deepseek) === accepted)
        precondition(APIKeyClient.calls == calls)
        var cached: [SmartCleanup.Pending] = []
        for i in 0..<40 {
            cached.append(smart.request("Wait, wait cache \(i)", entries: [], provider: .deepseek)!)
            try! await Task.sleep(nanoseconds: 5_000_000)
            precondition(cached.last!.result?.outcome == "used")
        }
        precondition(smart.request("Wait, wait cache 0", entries: [], provider: .deepseek) !== cached[0])
        precondition(smart.request("Wait, wait cache 39", entries: [], provider: .deepseek) === cached[39])
        APIKeyClient.slow = true
        let deadline = smart.request("Wait, wait wait for deadline", entries: [], provider: .deepseek)!
        try! await Task.sleep(nanoseconds: 10_200_000_000)
        precondition(deadline.result?.outcome == "cancelled")
        // Speculative requests stop 4 below the budget; the request at release can use those 4.
        let budget = SmartCleanup()
        budget.requestsPerMinute = 6
        let early = (0..<3).map { budget.request("Wait, wait early \($0)", entries: [], provider: .deepseek, speculative: true) }
        precondition(early[0] != nil && early[1] != nil && early[2] == nil)
        let final = (0..<5).map { budget.request("Wait, wait final \($0)", entries: [], provider: .deepseek)! }
        precondition(final.prefix(4).allSatisfy { $0.result?.outcome != "limited" } && final[4].result?.outcome == "limited")
        (early.compactMap { $0 } + final).forEach { budget.cancel($0) }
        print("Passed 54 cleanup integrity checks and request limit, budget, cancellation, cache, and deadline checks")
    }
}
