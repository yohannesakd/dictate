import Foundation

final class DeepSeekClient: CleanupClient {
    static var slow = true
    static var ignoreCancellation = false
    static var calls = 0
    static var cancellations = 0
    @MainActor func complete(instructions: String, message: String, timing: RequestTiming) async throws -> String {
        let ignoreCancellation = Self.ignoreCancellation
        Self.calls += 1
        do {
            try Task.checkCancellation()
            if Self.slow { try await Task.sleep(nanoseconds: 60_000_000_000) }
        } catch { Self.cancellations += 1; if !ignoreCancellation { throw error } }
        return message.components(separatedBy: "<transcript>").last!.replacingOccurrences(of: "</transcript>", with: "")
    }
    func warm() {}
}
final class ChatGPTClient: CleanupClient {
    @MainActor func complete(instructions: String, message: String, timing: RequestTiming) async throws -> String { throw CancellationError() }
    func warm() {}
}
