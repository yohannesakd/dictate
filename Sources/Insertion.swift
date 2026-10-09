// Types text into the focused field with synthetic key events (the clipboard is not used).
// Before typing, it reads the text before the cursor through Accessibility, where the app exposes it,
// to add a separating space and to avoid a capital letter in the middle of a sentence.

import AppKit
import ApplicationServices

enum Insertion {
    /// Serial, so two insertions never interleave their characters.
    private static let queue = DispatchQueue(label: "dictate.typing", qos: .userInitiated)
    private static var lastTarget: AXUIElement?

    static func focusedElement() -> AXUIElement? {
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(AXUIElementCreateSystemWide(), kAXFocusedUIElementAttribute as CFString, &value) == .success,
              let value, CFGetTypeID(value) == AXUIElementGetTypeID() else { return nil }
        return (value as! AXUIElement)
    }

    static func isSame(_ a: AXUIElement?, _ b: AXUIElement?) -> Bool {
        guard let a, let b else { return false }
        return CFEqual(a, b)
    }

    /// Adapts the text to its surroundings, then types it. `completion` runs on the main queue with the typing time.
    static func insert(_ text: String, completion: @escaping (Int) -> Void) {
        let target = focusedElement()
        let before = target.flatMap { textBeforeCursor(in: $0) }
        let adapted = adapt(text, before: before, typedHereLast: isSame(target, lastTarget))
        lastTarget = target
        queue.async {
            let start = Date()
            type(adapted)
            let ms = Int(Date().timeIntervalSince(start) * 1000)
            DispatchQueue.main.async { completion(ms) }
        }
    }

    /// Up to 24 characters before the insertion point. "" at the start of the field; nil when the app does not expose them.
    private static func textBeforeCursor(in element: AXUIElement) -> String? {
        var rangeValue: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, kAXSelectedTextRangeAttribute as CFString, &rangeValue) == .success,
              let rangeValue, CFGetTypeID(rangeValue) == AXValueGetTypeID() else { return nil }
        var selection = CFRange()
        guard AXValueGetValue(rangeValue as! AXValue, .cfRange, &selection) else { return nil }
        if selection.location <= 0 { return "" }
        var before = CFRange(location: max(0, selection.location - 24), length: min(24, selection.location))
        if let rangeArgument = AXValueCreate(.cfRange, &before) {
            var text: CFTypeRef?
            if AXUIElementCopyParameterizedAttributeValue(element, kAXStringForRangeParameterizedAttribute as CFString,
                                                         rangeArgument, &text) == .success, let string = text as? String {
                return string
            }
        }
        // Fallback: slice the whole value. Accessibility ranges count UTF-16 code units.
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, kAXValueAttribute as CFString, &value) == .success,
              let string = value as? String else { return nil }
        let units = Array(string.utf16)
        guard before.location + before.length <= units.count else { return nil }
        return String(utf16CodeUnits: Array(units[before.location..<(before.location + before.length)]), count: before.length)
    }

    static func adapt(_ text: String, before: String?, typedHereLast: Bool) -> String {
        guard let first = text.first else { return text }
        guard let before else {
            // The app hides its text. After our own dictation in the same field, the cursor is right after its last character.
            return typedHereLast ? " " + text : text
        }
        guard let last = before.last else { return text }  // Start of the field.
        let needsSpace = !last.isWhitespace && !"([{\"'“‘/-".contains(last) && !first.isPunctuation
        let trimmed = before.trimmingCharacters(in: .whitespaces)
        let sentenceStart = trimmed.isEmpty || trimmed.last.map { ".!?:\n".contains($0) } == true
        var result = text
        if !sentenceStart { result = lowercasingFirstWord(result) }
        return needsSpace ? " " + result : result
    }

    /// "And then" -> "and then" in the middle of a sentence. Only common words change, so names keep their capital.
    private static func lowercasingFirstWord(_ text: String) -> String {
        let word = text.prefix { $0.isLetter || $0 == "'" || $0 == "’" }
        guard word.count > 0, word != "I", !word.hasPrefix("I'"), !word.hasPrefix("I’"),
              word.dropFirst().allSatisfy({ !$0.isUppercase }), commonWords.contains(word.lowercased()) else { return text }
        return text.prefix(1).lowercased() + text.dropFirst()
    }

    private static func type(_ text: String) {
        let units = Array(text.utf16)
        let source = CGEventSource(stateID: .hidSystemState)
        var start = 0
        while start < units.count {
            var end = min(start + 64, units.count)
            if end < units.count, UTF16.isLeadSurrogate(units[end - 1]) { end -= 1 }  // Keep surrogate pairs together.
            let chunk = Array(units[start..<end])
            for keyDown in [true, false] {
                guard let event = CGEvent(keyboardEventSource: source, virtualKey: 0, keyDown: keyDown) else { continue }
                event.flags = []  // Ignore modifiers the user may still hold, such as fn.
                chunk.withUnsafeBufferPointer { event.keyboardSetUnicodeString(stringLength: chunk.count, unicodeString: $0.baseAddress) }
                event.post(tap: .cghidEventTap)
            }
            start = end
            if start < units.count { usleep(2000) }  // Some apps drop characters when events arrive too fast.
        }
    }

    private static let commonWords: Set<String> = [
        "a", "about", "actually", "after", "again", "all", "also", "although", "an", "and", "another", "any", "are", "as", "at",
        "be", "because", "been", "before", "both", "but", "by", "can", "could", "did", "do", "does", "each", "even", "every",
        "for", "from", "get", "go", "going", "got", "had", "has", "have", "he", "her", "here", "his", "how", "if", "in", "into",
        "is", "it", "it's", "its", "just", "know", "let", "let's", "like", "make", "maybe", "might", "more", "most", "must", "my",
        "need", "no", "not", "now", "of", "okay", "on", "one", "only", "or", "other", "our", "please", "really", "see", "she",
        "should", "since", "so", "some", "still", "such", "than", "thanks", "that", "the", "their", "them", "then", "there",
        "these", "they", "think", "this", "those", "though", "to", "too", "two", "until", "us", "use", "very", "want", "was",
        "we", "well", "were", "what", "when", "where", "which", "while", "who", "why", "will", "with", "would", "yes", "you",
        "your",
    ]
}
