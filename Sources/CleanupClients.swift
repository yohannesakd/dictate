// The smart cleanup models: Groq and DeepSeek with an API key, and GPT-6-Luna with a ChatGPT sign-in.

import AppKit
import CryptoKit
import Network

// MARK: API key providers

/// A chat-completions API with an API key: Groq and DeepSeek.
final class APIKeyClient: CleanupClient {
    let provider: CleanupProvider
    private let endpoint: URL
    private let options: [String: Any]
    private var keychainAccount: String { "\(provider.rawValue)-api-key" }

    init(_ provider: CleanupProvider) {
        self.provider = provider
        switch provider {
        case .groq:
            // Measured from the owner's Mac: about 190 ms median for a cleanup. Thinking off; it only adds time here.
            endpoint = URL(string: "https://api.groq.com/openai/v1/chat/completions")!
            options = ["model": "qwen/qwen3.8-27b", "reasoning_effort": "none"]
        default:
            endpoint = URL(string: "https://api.deepseek.com/chat/completions")!
            options = ["model": "deepseek-chat"]
        }
    }

    var hasKey: Bool { Keychain.read(keychainAccount) != nil }

    func setKey(_ key: String?) throws {
        let status = Keychain.write(keychainAccount, key.map { Data($0.utf8) })
        guard status == errSecSuccess else {
            throw CleanupError.setup("\(provider.title) key could not be saved (Keychain status \(status)).")
        }
    }

    func complete(instructions: String, message: String, timing: RequestTiming) async throws -> String {
        let name = provider.title
        guard let key = Keychain.read(keychainAccount).flatMap({ String(data: $0, encoding: .utf8) }) else {
            throw CleanupError.setup("Add a \(name) API key in Dictate.")
        }
        var request = URLRequest(url: endpoint)
        request.httpMethod = "POST"
        request.setValue("Bearer \(key)", forHTTPHeaderField: "Authorization")
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = try JSONSerialization.data(withJSONObject: options.merging([
            "temperature": 0, "max_tokens": 4096,
            "messages": [["role": "system", "content": instructions], ["role": "user", "content": message]],
        ] as [String: Any]) { $1 })
        let (data, response) = try await URLSession.shared.data(for: request, delegate: timing)
        let status = (response as? HTTPURLResponse)?.statusCode ?? 0
        guard status == 200 else {
            switch status {
            case 401: throw CleanupError.setup("\(name) rejected the API key.")
            case 429: throw CleanupError.server("\(name) rate limit reached: \(serverMessage(data))")
            default: throw CleanupError.server("\(name) error \(status): \(serverMessage(data))")
            }
        }
        let json = try JSONSerialization.jsonObject(with: data) as? [String: Any]
        let usage = json?["usage"] as? [String: Any]
        let cached = usage?["prompt_cache_hit_tokens"] as? Int  // DeepSeek
            ?? (usage?["prompt_tokens_details"] as? [String: Any])?["cached_tokens"] as? Int
        timing.setTokens(input: usage?["prompt_tokens"] as? Int, output: usage?["completion_tokens"] as? Int, cached: cached)
        let choice = (json?["choices"] as? [[String: Any]])?.first
        // "length" means the reply was cut off. A shortened cleanup could pass the guard and lose words.
        guard choice?["finish_reason"] as? String == "stop",
              let text = (choice?["message"] as? [String: Any])?["content"] as? String else {
            throw CleanupError.server("\(name) sent no complete reply.")
        }
        return text.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    func warm() { warmConnection(to: endpoint) }
}

// MARK: ChatGPT

/// Calls the endpoint that the Codex CLI and Pi use with a ChatGPT sign-in. It is not a public API, so OpenAI can change it.
final class ChatGPTClient: CleanupClient {
    let auth = ChatGPTAuth()
    private let endpoint = URL(string: "https://chatgpt.com/backend-api/codex/responses")!

    func complete(instructions: String, message: String, timing: RequestTiming) async throws -> String {
        let token = try await auth.token()
        var request = URLRequest(url: endpoint)
        request.httpMethod = "POST"
        request.setValue("Bearer \(token.access)", forHTTPHeaderField: "Authorization")
        request.setValue(token.accountID, forHTTPHeaderField: "chatgpt-account-id")
        request.setValue("dictate", forHTTPHeaderField: "originator")
        request.setValue("responses=experimental", forHTTPHeaderField: "OpenAI-Beta")
        request.setValue("text/event-stream", forHTTPHeaderField: "Accept")
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        // The endpoint accepts only streamed responses.
        request.httpBody = try JSONSerialization.data(withJSONObject: [
            "model": "gpt-6-luna", "store": false, "stream": true, "instructions": instructions,
            "input": [["role": "user", "content": [["type": "input_text", "text": message]]]],
            "reasoning": ["effort": "none"], "text": ["verbosity": "low"], "prompt_cache_key": "dictate",
        ] as [String: Any])
        let (bytes, response) = try await URLSession.shared.bytes(for: request, delegate: timing)
        let status = (response as? HTTPURLResponse)?.statusCode ?? 0
        guard status == 200 else {
            var body = Data()
            for try await byte in bytes.prefix(2000) { body.append(byte) }
            if status == 401 { await auth.expire() }
            throw CleanupError.server("ChatGPT error \(status): \(serverMessage(body))")
        }
        var text = ""
        var completed = false
        for try await line in bytes.lines where line.hasPrefix("data:") {
            guard let event = try? JSONSerialization.jsonObject(with: Data(line.dropFirst(5).utf8)) as? [String: Any] else { continue }
            switch event["type"] as? String {
            case "response.output_text.delta": text += event["delta"] as? String ?? ""
            case "response.completed":
                completed = true
                let usage = (event["response"] as? [String: Any])?["usage"] as? [String: Any]
                timing.setTokens(input: usage?["input_tokens"] as? Int, output: usage?["output_tokens"] as? Int,
                                 cached: (usage?["input_tokens_details"] as? [String: Any])?["cached_tokens"] as? Int)
            case "response.failed", "error": throw CleanupError.server("ChatGPT could not answer: \(event)")
            default: break
            }
        }
        guard completed else { throw CleanupError.server("ChatGPT sent no complete reply.") }
        return text.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    func warm() { warmConnection(to: endpoint) }
}

/// Sign in with ChatGPT: the OAuth flow of the Codex CLI and Pi. Dictate keeps its own tokens in the Keychain,
/// so its sign-in does not change the Codex or Pi sign-in.
actor ChatGPTAuth {
    struct Token: Codable {
        var access: String
        var refresh: String
        var expires: Date
        var accountID: String
    }

    private let clientID = "app_EMoamEEZ73f0CkXaXp7hrann"
    private let tokenURL = URL(string: "https://auth.openai.com/oauth/token")!
    private let redirectURI = "http://localhost:1455/auth/callback"
    private let keychainAccount = "chatgpt-token"
    private var cached: Token?
    private var refreshing: Task<Token, Error>?

    nonisolated var isSignedIn: Bool { Keychain.read(keychainAccount) != nil }

    func token() async throws -> Token {
        guard var token = cached ?? Keychain.read(keychainAccount).flatMap({ try? JSONDecoder().decode(Token.self, from: $0) }) else {
            throw CleanupError.setup("Sign in with ChatGPT in Dictate.")
        }
        cached = token
        if token.expires > Date().addingTimeInterval(60) { return token }
        // One refresh at a time: a refresh token works once.
        let task = refreshing ?? Task { try await self.requestToken(["grant_type": "refresh_token", "refresh_token": token.refresh]) }
        refreshing = task
        defer { refreshing = nil }
        token = try await task.value
        save(token)
        return token
    }

    /// The server rejected the access token. The next request refreshes it.
    func expire() {
        cached?.expires = .distantPast
    }

    func signIn() async throws {
        let verifier = randomString()
        let challenge = Data(SHA256.hash(data: Data(verifier.utf8))).base64URL
        let state = randomString()
        var url = URLComponents(string: "https://auth.openai.com/oauth/authorize")!
        url.queryItems = [
            "response_type": "code", "client_id": clientID, "redirect_uri": redirectURI,
            "scope": "openid profile email offline_access", "code_challenge": challenge,
            "code_challenge_method": "S256", "state": state, "id_token_add_organizations": "true",
            "codex_cli_simplified_flow": "true", "originator": "dictate",
        ].map { URLQueryItem(name: $0.key, value: $0.value) }
        let callback = try startCallbackServer(state: state)
        let authorizeURL = url.url!
        await MainActor.run { _ = NSWorkspace.shared.open(authorizeURL) }
        let code = try await callback.value
        save(try await requestToken([
            "grant_type": "authorization_code", "code": code, "code_verifier": verifier, "redirect_uri": redirectURI,
        ]))
    }

    func signOut() {
        cached = nil
        Keychain.write(keychainAccount, nil)
    }

    private func save(_ token: Token) {
        cached = token
        Keychain.write(keychainAccount, try? JSONEncoder().encode(token))
    }

    private func requestToken(_ fields: [String: String]) async throws -> Token {
        var request = URLRequest(url: tokenURL)
        request.httpMethod = "POST"
        request.setValue("application/x-www-form-urlencoded", forHTTPHeaderField: "Content-Type")
        let allowed = CharacterSet.alphanumerics.union(CharacterSet(charactersIn: "-._~"))
        let form = fields.merging(["client_id": clientID]) { $1 }.map { key, value in
            "\(key)=\(value.addingPercentEncoding(withAllowedCharacters: allowed) ?? value)"
        }
        request.httpBody = Data(form.joined(separator: "&").utf8)
        let (data, response) = try await URLSession.shared.data(for: request)
        let json = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] ?? [:]
        guard (response as? HTTPURLResponse)?.statusCode == 200,
              let access = json["access_token"] as? String, let refresh = json["refresh_token"] as? String,
              let expiresIn = json["expires_in"] as? Double, let accountID = Self.accountID(access) else {
            throw CleanupError.setup("ChatGPT sign-in failed: \(serverMessage(data)). Sign in again in Dictate.")
        }
        return Token(access: access, refresh: refresh, expires: Date().addingTimeInterval(expiresIn), accountID: accountID)
    }

    private static func accountID(_ jwt: String) -> String? {
        let parts = jwt.split(separator: ".")
        guard parts.count == 3 else { return nil }
        var payload = parts[1].replacingOccurrences(of: "-", with: "+").replacingOccurrences(of: "_", with: "/")
        payload += String(repeating: "=", count: (4 - payload.count % 4) % 4)
        let claims = Data(base64Encoded: payload).flatMap { try? JSONSerialization.jsonObject(with: $0) as? [String: Any] }
        return (claims?["https://api.openai.com/auth"] as? [String: Any])?["chatgpt_account_id"] as? String
    }

    /// Receives the browser redirect on localhost:1455, the port that the OAuth client allows.
    private func startCallbackServer(state: String) throws -> Task<String, Error> {
        let parameters = NWParameters.tcp
        parameters.requiredLocalEndpoint = .hostPort(host: "127.0.0.1", port: 1455)
        let listener = try NWListener(using: parameters)
        return Task {
            try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<String, Error>) in
                var finished = false
                func finish(_ result: Result<String, Error>) {
                    guard !finished else { return }
                    finished = true
                    listener.cancel()
                    continuation.resume(with: result)
                }
                listener.newConnectionHandler = { connection in
                    connection.start(queue: .main)
                    connection.receive(minimumIncompleteLength: 1, maximumLength: 65536) { data, _, _, _ in
                        let requestLine = data.flatMap { String(data: $0, encoding: .utf8) }?.split(separator: " ")
                        let path = requestLine.flatMap { $0.count > 1 ? String($0[1]) : nil } ?? ""
                        let items = URLComponents(string: "http://localhost" + path)?.queryItems ?? []
                        let code = items.first { $0.name == "code" }?.value
                        let valid = code != nil && items.first { $0.name == "state" }?.value == state
                        let body = valid ? "Dictate is signed in to ChatGPT. You can close this window." : "Sign-in failed. Try again in Dictate."
                        let reply = "HTTP/1.1 200 OK\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: \(body.utf8.count)\r\nConnection: close\r\n\r\n\(body)"
                        connection.send(content: Data(reply.utf8), completion: .contentProcessed { _ in connection.cancel() })
                        guard path.hasPrefix("/auth/callback") else { return }
                        finish(valid ? .success(code!) : .failure(CleanupError.setup("ChatGPT sign-in failed.")))
                    }
                }
                listener.stateUpdateHandler = { state in
                    if case .failed = state {
                        finish(.failure(CleanupError.setup("Port 1455 is in use. Close other ChatGPT sign-ins, then try again.")))
                    }
                }
                listener.start(queue: .main)
                DispatchQueue.main.asyncAfter(deadline: .now() + 300) { finish(.failure(CleanupError.setup("Sign-in timed out."))) }
            }
        }
    }

    private func randomString() -> String {
        var bytes = [UInt8](repeating: 0, count: 32)
        _ = SecRandomCopyBytes(kSecRandomDefault, bytes.count, &bytes)
        return Data(bytes).base64URL
    }
}

// MARK: Shared

private func warmConnection(to url: URL) {
    var request = URLRequest(url: url)
    request.httpMethod = "HEAD"
    let task = URLSession.shared.dataTask(with: request)
    task.delegate = RequestTiming()  // Records the round trip of the connection it opens.
    task.resume()
}

private func serverMessage(_ data: Data) -> String {
    let json = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any]
    let error = json?["error"]
    return (error as? [String: Any])?["message"] as? String ?? error as? String
        ?? String(data: data.prefix(200), encoding: .utf8) ?? "no details"
}

private extension Data {
    var base64URL: String {
        base64EncodedString().replacingOccurrences(of: "+", with: "-").replacingOccurrences(of: "/", with: "_")
            .replacingOccurrences(of: "=", with: "")
    }
}

/// Secrets of this app: one generic password per account name.
enum Keychain {
    private static let service = "dev.local.dictate"

    static func read(_ account: String) -> Data? {
        var result: AnyObject?
        let query: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service,
                                    kSecAttrAccount as String: account, kSecReturnData as String: true]
        return SecItemCopyMatching(query as CFDictionary, &result) == errSecSuccess ? result as? Data : nil
    }

    /// Nil deletes the secret.
    @discardableResult
    static func write(_ account: String, _ data: Data?) -> OSStatus {
        let query: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service,
                                    kSecAttrAccount as String: account]
        guard let data else {
            let status = SecItemDelete(query as CFDictionary)
            return status == errSecItemNotFound ? errSecSuccess : status
        }
        let status = SecItemUpdate(query as CFDictionary, [kSecValueData as String: data] as CFDictionary)
        guard status == errSecItemNotFound else { return status }
        return SecItemAdd(query.merging([kSecValueData as String: data]) { $1 } as CFDictionary, nil)
    }
}
