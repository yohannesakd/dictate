import Foundation

final class DeepSeekClient: CleanupClient {
    static var slow = true
    static var calls = 0
    static var cancellations = 0
    func complete(instructions: String, message: String) async throws -> String {
        Self.calls += 1
        do {
            try Task.checkCancellation()
            if Self.slow { try await Task.sleep(nanoseconds: 60_000_000_000) }
        } catch { Self.cancellations += 1; throw error }
        return message.components(separatedBy: "<transcript>").last!.replacingOccurrences(of: "</transcript>", with: "")
    }
    func warm() {}
}
final class ChatGPTClient: CleanupClient {
    func complete(instructions: String, message: String) async throws -> String { throw CancellationError() }
    func warm() {}
}
