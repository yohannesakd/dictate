// The transcript log: every dictation, kept on this Mac without a limit, so Dictate can learn from it later.
// One JSON object per line: the raw transcript, the rule result, the cleanup outcome, the model, prompt version, and
// reply, the inserted text, and the app. The id matches the dictation's line in the metrics log.

import Foundation

enum TranscriptLog {
    static let url = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        .appendingPathComponent("Dictate/transcripts.jsonl")
    private static let queue = DispatchQueue(label: "dictate.transcripts", qos: .utility)

    /// Nil values are left out.
    static func write(_ entry: [String: Any?]) {
        var record = entry.compactMapValues { $0 }
        record["date"] = ISO8601DateFormatter().string(from: Date())
        queue.async { appendJSONLine(record, to: url) }
    }
}
