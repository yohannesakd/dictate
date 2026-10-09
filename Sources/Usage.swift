// Words per day and recent release-to-text times, for the home screen.

import Foundation

struct Usage: Codable {
    struct Day: Codable {
        var words = 0
        var dictations = 0
        var seconds = 0.0
    }

    /// Keyed by local date, "yyyy-MM-dd". Only the last 14 days are kept.
    private(set) var days: [String: Day] = [:]
    /// The most recent times from fn release to the end of typing, in milliseconds.
    private(set) var latencies: [Int] = []

    private static let keepDays = 14
    private static let keepLatencies = 50

    mutating func addDictation(words: Int, seconds: Double, on date: Date = Date()) {
        let key = Self.key(date)
        days[key, default: Day()].words += words
        days[key, default: Day()].dictations += 1
        days[key, default: Day()].seconds += seconds
        let oldest = Self.key(Calendar.current.date(byAdding: .day, value: -(Self.keepDays - 1), to: date) ?? date)
        days = days.filter { $0.key >= oldest }
    }

    mutating func addLatency(_ ms: Int) {
        latencies = Array((latencies + [ms]).suffix(Self.keepLatencies))
    }

    func day(_ date: Date) -> Day { days[Self.key(date)] ?? Day() }

    /// The last 7 days, oldest first, ending today.
    func week(endingOn today: Date = Date()) -> [(date: Date, day: Day)] {
        (0..<7).reversed().compactMap { offset in
            Calendar.current.date(byAdding: .day, value: -offset, to: today).map { ($0, day($0)) }
        }
    }

    var medianLatency: Int? {
        guard !latencies.isEmpty else { return nil }
        return latencies.sorted()[latencies.count / 2]
    }

    /// Speaking speed over the last 7 days. Nil until there is a minute of speech.
    var wordsPerMinute: Int? {
        let week = week()
        let seconds = week.reduce(0) { $0 + $1.day.seconds }
        guard seconds >= 60 else { return nil }
        return Int((Double(week.reduce(0) { $0 + $1.day.words }) / seconds * 60).rounded())
    }

    private static let formatter: DateFormatter = {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.dateFormat = "yyyy-MM-dd"
        return formatter
    }()

    private static func key(_ date: Date) -> String { formatter.string(from: date) }
}
