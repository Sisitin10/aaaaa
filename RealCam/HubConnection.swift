import Foundation
import UIKit

@MainActor
final class HubConnection {
    var onStateChange: ((Bool, String) -> Void)?
    var onImage: ((UIImage) -> Void)?

    private var task: URLSessionWebSocketTask?
    private var session: URLSession?
    private var receiveTask: Task<Void, Never>?

    func connect(host: String, code: String) {
        disconnect()
        let hostWithPort = host.contains(":") && !host.hasPrefix("[") ? "[\(host)]" : host
        guard let url = URL(string: "ws://\(hostWithPort):47821/control?code=\(code)") else {
            onStateChange?(false, "Invalid PC address")
            return
        }
        let configuration = URLSessionConfiguration.default
        configuration.timeoutIntervalForRequest = 8
        let session = URLSession(configuration: configuration)
        let socket = session.webSocketTask(with: url)
        self.session = session
        self.task = socket
        socket.resume()
        onStateChange?(false, "Connecting…")
        receiveTask = Task { [weak self] in
            guard let self else { return }
            do {
                while !Task.isCancelled {
                    let message = try await socket.receive()
                    switch message {
                    case .data(let bytes):
                        if let image = UIImage(data: bytes) { self.onImage?(image) }
                    case .string(let text):
                        if text == "ready" { self.onStateChange?(true, "Connected to FH6 Hub") }
                        else if text.hasPrefix("error:") { self.onStateChange?(false, String(text.dropFirst(6))) }
                    @unknown default:
                        break
                    }
                }
            } catch {
                guard !Task.isCancelled else { return }
                self.onStateChange?(false, "Connection lost: \(error.localizedDescription)")
            }
        }
    }

    func send(_ payload: [String: Any]) {
        guard let task, let data = try? JSONSerialization.data(withJSONObject: payload),
              let text = String(data: data, encoding: .utf8) else { return }
        task.send(.string(text)) { [weak self] error in
            guard let error else { return }
            Task { @MainActor in self?.onStateChange?(false, "Send failed: \(error.localizedDescription)") }
        }
    }

    func sendAction(_ action: String) {
        send(["type": "action", "action": action])
    }

    func disconnect() {
        receiveTask?.cancel()
        receiveTask = nil
        task?.cancel(with: .goingAway, reason: nil)
        task = nil
        session?.invalidateAndCancel()
        session = nil
        onStateChange?(false, "Not connected")
    }
}
