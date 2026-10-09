// Per-dictation resource metrics, written as JSON lines to ~/Library/Logs/Dictate/metrics.jsonl.

import Darwin
import Foundation

enum ProcessStats {
    /// User plus system CPU time of this process, in seconds.
    static func cpuSeconds() -> Double {
        var usage = rusage()
        getrusage(RUSAGE_SELF, &usage)
        func seconds(_ t: timeval) -> Double { Double(t.tv_sec) + Double(t.tv_usec) / 1_000_000 }
        return seconds(usage.ru_utime) + seconds(usage.ru_stime)
    }

    /// Current and peak physical footprint (the number Activity Monitor shows as Memory), in MB.
    static func footprintMB() -> (current: Double, peak: Double) {
        var info = task_vm_info_data_t()
        var count = mach_msg_type_number_t(MemoryLayout<task_vm_info_data_t>.size / MemoryLayout<integer_t>.size)
        _ = withUnsafeMutablePointer(to: &info) {
            $0.withMemoryRebound(to: integer_t.self, capacity: Int(count)) {
                task_info(mach_task_self_, task_flavor_t(TASK_VM_INFO), $0, &count)
            }
        }
        return (Double(info.phys_footprint) / 1_048_576, Double(info.ledger_phys_footprint_peak) / 1_048_576)
    }
}

/// Measures one stretch of work: wall time and process CPU time.
struct Stopwatch {
    let started: Double
    init(started: Double = Monotonic.now) { self.started = started }
    private let cpu = ProcessStats.cpuSeconds()

    var wallMs: Int { Monotonic.milliseconds(since: started) }
    var cpuMs: Int { Int((ProcessStats.cpuSeconds() - cpu) * 1000) }
}

enum MetricsLog {
    static let url = FileManager.default.homeDirectoryForCurrentUser
        .appendingPathComponent("Library/Logs/Dictate/metrics.jsonl")
    private static let queue = DispatchQueue(label: "dictate.metrics", qos: .utility)

    static func write(_ fields: [String: Any]) {
        var fields = fields
        let memory = ProcessStats.footprintMB()
        fields["date"] = ISO8601DateFormatter().string(from: Date())
        fields["footprint_mb"] = Int(memory.current)
        fields["peak_footprint_mb"] = Int(memory.peak)
        queue.async { appendJSONLine(fields, to: url) }
    }
}

/// Appends one JSON object as a line. Callers serialize writes to the same file on one queue.
func appendJSONLine(_ object: [String: Any], to url: URL) {
    guard let data = try? JSONSerialization.data(withJSONObject: object, options: [.sortedKeys]) else { return }
    try? FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
    if let handle = try? FileHandle(forWritingTo: url) {
        handle.seekToEndOfFile()
        handle.write(data + Data("\n".utf8))
        try? handle.close()
    } else {
        try? (data + Data("\n".utf8)).write(to: url)
    }
}
