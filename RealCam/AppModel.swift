import Foundation
import SwiftUI
import UIKit

@MainActor
final class AppModel: ObservableObject {
    @Published var host = ""
    @Published var pairingCode = ""
    @Published var connectionStatus = "Not connected"
    @Published var isConnected = false
    @Published var previewImage: UIImage?
    @Published var previewFPS = 0
    @Published var zoom: Double = 0.5
    @Published var isTracking = false
    @Published var message: String?

    private let bridge = HubConnection()
    private let tracker = MotionTracker()
    private var poseTimer: Timer?
    private var previewFrameCount = 0
    private var lastFpsSampleTime = Date.timeIntervalSinceReferenceDate

    init() {
        bridge.onStateChange = { [weak self] connected, status in
            guard let self else { return }
            self.isConnected = connected
            self.connectionStatus = status
            if connected {
                self.bridge.send(["type": "zoom", "value": self.zoom])
                self.startPoseUpdates()
            }
            else {
                self.poseTimer?.invalidate()
                self.poseTimer = nil
                self.previewImage = nil
                self.previewFPS = 0
                self.previewFrameCount = 0
                self.lastFpsSampleTime = Date.timeIntervalSinceReferenceDate
            }
        }
        bridge.onImage = { [weak self] image in
            guard let self else { return }
            self.previewImage = image
            self.previewFrameCount += 1
            let now = Date.timeIntervalSinceReferenceDate
            let elapsed = now - self.lastFpsSampleTime
            if elapsed >= 1 {
                self.previewFPS = Int((Double(self.previewFrameCount) / elapsed).rounded())
                self.previewFrameCount = 0
                self.lastFpsSampleTime = now
            }
        }
        tracker.onPose = { [weak self] _ in self?.isTracking = true }
        tracker.onFailure = { [weak self] reason in
            self?.isTracking = false
            self?.message = reason
        }
    }

    func connect() {
        let cleanHost = host.trimmingCharacters(in: .whitespacesAndNewlines)
        let cleanCode = pairingCode.filter(\.isNumber)
        guard !cleanHost.isEmpty, cleanCode.count == 6 else {
            message = "Enter the PC’s local IP address and its six-digit pairing code."
            return
        }
        pairingCode = cleanCode
        connectionStatus = "Connecting…"
        bridge.connect(host: cleanHost, code: cleanCode)
    }

    func disconnect() {
        stopTracking()
        bridge.disconnect()
    }

    func startTracking() {
        tracker.start()
    }

    func recenter() {
        tracker.recenter()
        bridge.sendAction("recenter")
    }

    func stopTracking() {
        tracker.stop()
        isTracking = false
        bridge.sendAction("release")
    }

    func openPhotoMode() { bridge.sendAction("photoMode") }
    func resetCamera() { tracker.recenter(); bridge.sendAction("reset") }

    func setZoom(_ value: Double) {
        zoom = min(max(value, 0), 1)
        bridge.send(["type": "zoom", "value": zoom])
    }

    private func startPoseUpdates() {
        poseTimer?.invalidate()
        poseTimer = Timer.scheduledTimer(withTimeInterval: 1.0 / 30.0, repeats: true) { [weak self] _ in
            guard let self, self.isTracking else { return }
            let pose = self.tracker.pose
            self.bridge.send([
                "type": "pose",
                "x": pose.x, "y": pose.y, "z": pose.z,
                "yaw": pose.yaw, "pitch": pose.pitch
            ])
        }
    }
}
