import Foundation

@main struct CleanupChecks {
    static func main() {
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
            ("nyc nyc", "New York City New York City", true),
            ("Ask soul to help Bob", "Ask Bob to help Sol", false),
            ("Ask soul to help Bob", "Ask Sol to help Bob", true),
            ("Ask soul but my soul is tired", "Ask Sol but my soul is tired", true),
            ("Set it to 5.25", "Set it to 525", false),
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
        print("Passed 22 cleanup integrity checks")
    }
}
