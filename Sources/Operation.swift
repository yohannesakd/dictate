import Foundation

/// Shared by queued recognition and delivery. Cancellation never requires the main queue.
final class OperationCancellation: @unchecked Sendable {
    private let lock = NSLock()
    private var cancelled = false
    var isCancelled: Bool {
        lock.lock(); defer { lock.unlock() }
        return cancelled
    }
    func cancel() {
        lock.lock(); cancelled = true; lock.unlock()
    }
}

enum Monotonic {
    static var now: Double { ProcessInfo.processInfo.systemUptime }
    static func milliseconds(since start: Double) -> Int { Int(max(0, now - start) * 1000) }
}
