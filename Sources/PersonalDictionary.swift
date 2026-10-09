// The personal dictionary: "when the transcript says X, write Y". It fixes names and terms the model mishears.
// Matching is literal: whole words or phrases, case-insensitive, longest phrase first, one pass (no chains).

import Foundation

struct DictionaryEntry: Codable, Identifiable, Equatable {
    var id = UUID()
    var heard = ""
    var written = ""
}

enum PersonalDictionary {
    static func apply(_ text: String, entries: [DictionaryEntry]) -> String {
        var replacements: [String: String] = [:]
        for entry in entries {
            let key = normalize(entry.heard)
            let value = entry.written.trimmingCharacters(in: .whitespaces)
            if !key.isEmpty, !value.isEmpty { replacements[key] = value }
        }
        guard !replacements.isEmpty else { return text }

        // Words of a phrase may be separated by spaces or a comma the model added ("ask, soul").
        let alternatives = replacements.keys.sorted { $0.count > $1.count }.map { key in
            key.split(separator: " ").map { NSRegularExpression.escapedPattern(for: String($0)) }.joined(separator: #"[\s,]+"#)
        }
        let pattern = #"(?<![\p{L}\p{N}'’])(?:"# + alternatives.joined(separator: "|") + #")(?![\p{L}\p{N}'’])"#
        guard let regex = try? NSRegularExpression(pattern: pattern, options: [.caseInsensitive]) else { return text }

        var result = text
        let matches = regex.matches(in: text, range: NSRange(text.startIndex..., in: text))
        for match in matches.reversed() {
            guard let range = Range(match.range, in: result),
                  var replacement = replacements[normalize(String(result[range]))] else { continue }
            // "Ask soul" at a sentence start keeps its capital: "Ask Sol". A written form with its own capitals ("iPhone") wins.
            let firstWord = replacement.prefix { !$0.isWhitespace }
            if result[range].first?.isUppercase == true, firstWord.allSatisfy({ !$0.isUppercase }) {
                replacement = replacement.prefix(1).uppercased() + replacement.dropFirst()
            }
            result.replaceSubrange(range, with: replacement)
        }
        return result
    }

    private static func normalize(_ phrase: String) -> String {
        phrase.lowercased()
            .replacingOccurrences(of: ",", with: " ")
            .split(whereSeparator: \.isWhitespace)
            .joined(separator: " ")
    }
}
