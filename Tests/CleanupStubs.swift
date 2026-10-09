import Foundation

final class DeepSeekClient: CleanupClient {
    func complete(instructions: String, message: String) async throws -> String { throw CancellationError() }
    func warm() {}
}
final class ChatGPTClient: CleanupClient {
    func complete(instructions: String, message: String) async throws -> String { throw CancellationError() }
    func warm() {}
}
