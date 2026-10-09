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
        var result = text
        for match in replacements(in: text, entries: entries).reversed() {
            guard let range = Range(match.range, in: result) else { continue }
            var replacement = match.written
            // "Ask soul" at a sentence start keeps its capital: "Ask Sol". A written form with its own capitals ("iPhone") wins.
            let firstWord = replacement.prefix { !$0.isWhitespace }
            if result[range].first?.isUppercase == true, firstWord.allSatisfy({ !$0.isUppercase }) {
                replacement = replacement.prefix(1).uppercased() + replacement.dropFirst()
            }
            result.replaceSubrange(range, with: replacement)
        }
        return result
    }

    /// Supplies the same literal, longest-match locations to deterministic cleanup and the model guard.
    static func replacements(in text: String, entries: [DictionaryEntry]) -> [(range: NSRange, written: String)] {
        let (replacements, regex) = prepare(entries)
        guard let regex else { return [] }
        let value = text as NSString
        return regex.matches(in: text, range: NSRange(location: 0, length: value.length)).compactMap { match in
            guard let written = replacements[normalize(value.substring(with: match.range))] else { return nil }
            return (match.range, written)
        }
    }

    /// Normalizing, sorting, and compiling take up to about 2 ms, so they run again only when the entries change.
    /// Used on the main queue only.
    private static var prepared: (entries: [DictionaryEntry], replacements: [String: String], regex: NSRegularExpression?)?

    private static func prepare(_ entries: [DictionaryEntry]) -> ([String: String], NSRegularExpression?) {
        if let prepared, prepared.entries == entries { return (prepared.replacements, prepared.regex) }
        var replacements: [String: String] = [:]
        for entry in entries {
            let key = normalize(entry.heard)
            let value = entry.written.trimmingCharacters(in: .whitespaces)
            if !key.isEmpty, !value.isEmpty { replacements[key] = value }
        }
        var regex: NSRegularExpression?
        if !replacements.isEmpty {
            // Words of a phrase may be separated by spaces or a comma the model added ("ask, soul").
            let alternatives = replacements.keys.sorted { $0.count > $1.count }.map { key in
                key.split(separator: " ").map { NSRegularExpression.escapedPattern(for: String($0)) }.joined(separator: #"[\s,]+"#)
            }
            let pattern = #"(?<![\p{L}\p{N}'’])(?:"# + alternatives.joined(separator: "|") + #")(?![\p{L}\p{N}'’])"#
            regex = try? NSRegularExpression(pattern: pattern, options: [.caseInsensitive])
        }
        prepared = (entries, replacements, regex)
        return (replacements, regex)
    }

    static func normalize(_ phrase: String) -> String {
        phrase.lowercased()
            .replacingOccurrences(of: ",", with: " ")
            .split(whereSeparator: \.isWhitespace)
            .joined(separator: " ")
    }
}
