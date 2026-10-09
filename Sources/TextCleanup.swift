// Rule-based cleanup of a transcript: filler words, stutters, and "scratch that". Each rule can be turned off.
// It changes only what rules can decide safely. Corrections such as "at two, actually three" need a language model.

import Foundation

enum TextCleanup {
    struct Rules {
        var fillers = true
        var repeats = true
        var scratchThat = true
    }

    private static let fillers: Set<String> = ["um", "umm", "uh", "uhh", "uhm", "erm", "er", "ah", "ahh", "hm", "hmm", "mm", "mmm"]
    // Only short function words count as stutters. "very very" or "no no" can be meant, so they stay.
    private static let stutterWords: Set<String> = [
        "i", "a", "an", "the", "to", "and", "but", "or", "so", "of", "in", "on", "at", "for", "with", "is", "it", "we",
        "you", "he", "she", "they", "my", "this", "can", "will", "would", "just", "if", "be", "was", "are", "have",
    ]

    static func clean(_ text: String, rules: Rules) -> String {
        var words = (rules.scratchThat ? applyScratchThat(text) : text).split(separator: " ").map(String.init)
        if rules.fillers { words = removeFillers(words) }
        if rules.repeats { words = removeStutters(words) }
        let result = words.joined(separator: " ")
        return result == text ? text : capitalizeFirst(result)
    }

    private static func core(_ word: String) -> String {
        word.trimmingCharacters(in: .punctuationCharacters).lowercased()
    }

    /// "Send it to John, scratch that, send it to Sarah." becomes "Send it to Sarah."
    /// The clause before "scratch that" is removed back to the previous sentence end.
    private static func applyScratchThat(_ text: String) -> String {
        var result = text
        while let marker = result.range(of: #"[,;.]?\s*\bscratch that\b[,;.]?\s*"#, options: [.regularExpression, .caseInsensitive]) {
            let before = result[..<marker.lowerBound]
            let keepEnd = before.lastIndex(where: { ".!?".contains($0) }).map { before.index(after: $0) } ?? before.startIndex
            result = String(result[..<keepEnd]) + " " + capitalizeFirst(String(result[marker.upperBound...]))
            result = result.trimmingCharacters(in: .whitespaces)
        }
        return result
    }

    private static func removeFillers(_ words: [String]) -> [String] {
        var out: [String] = []
        var capitalizeNext = false
        for word in words {
            guard fillers.contains(core(word)) else {
                out.append(capitalizeNext ? capitalizeFirst(word) : word)
                capitalizeNext = false
                continue
            }
            guard var previous = out.popLast() else {
                capitalizeNext = true  // A filler opened the text.
                continue
            }
            if endsSentence(previous) {
                capitalizeNext = true  // "...think. Uh, can you" -> "...think. Can you"
            } else if endsSentence(word) {
                // "...the logs um." keeps the sentence end on the previous word.
                previous = previous.trimmingCharacters(in: CharacterSet(charactersIn: ",;")) + String(word.last!)
            } else if word.hasSuffix(","), previous.hasSuffix(",") {
                // "can you, um, check" -> "can you check": the commas only bracketed the filler.
                previous.removeLast()
            }
            out.append(previous)
        }
        return out
    }

    private static func endsSentence(_ word: String) -> Bool {
        word.last.map { ".!?".contains($0) } ?? false
    }

    private static func removeStutters(_ words: [String]) -> [String] {
        var out: [String] = []
        for word in words {
            if let previous = out.last,
               core(previous) == core(word), stutterWords.contains(core(word)),
               previous.last.map({ $0.isLetter || $0.isNumber }) ?? false {
                // "I I want" -> "I want". Keep the later copy, which carries any punctuation.
                out[out.count - 1] = previous.first?.isUppercase == true ? capitalizeFirst(word) : word
                continue
            }
            out.append(word)
        }
        return out
    }

    private static func capitalizeFirst(_ word: String) -> String {
        word.prefix(1).uppercased() + word.dropFirst()
    }
}
