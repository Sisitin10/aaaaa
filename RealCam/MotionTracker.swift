import ARKit
import Foundation
import simd

final class MotionTracker: NSObject, ARSessionDelegate {
    private let session = ARSession()
    private var origin: simd_float4x4?
    private(set) var pose = CameraPose()
    var onPose: ((CameraPose) -> Void)?
    var onFailure: ((String) -> Void)?

    override init() {
        super.init()
        session.delegate = self
    }

    func start() {
        guard ARWorldTrackingConfiguration.isSupported else {
            onFailure?("This iPhone does not support ARKit world tracking.")
            return
        }
        let configuration = ARWorldTrackingConfiguration()
        configuration.worldAlignment = .gravity
        origin = nil
        pose = CameraPose()
        session.run(configuration, options: [.resetTracking, .removeExistingAnchors])
    }

    func recenter() {
        guard let frame = session.currentFrame else { return }
        origin = frame.camera.transform
        pose = CameraPose()
        onPose?(pose)
    }

    func stop() {
        session.pause()
        origin = nil
        pose = CameraPose()
    }

    func session(_ session: ARSession, didUpdate frame: ARFrame) {
        let transform = frame.camera.transform
        if origin == nil { origin = transform }
        guard let origin else { return }
        let relative = simd_inverse(origin) * transform
        let translation = SIMD3<Float>(relative.columns.3.x, relative.columns.3.y, relative.columns.3.z)
        let rotation = simd_quatf(relative)
        let q = rotation.vector
        let yaw = atan2(2 * (q.w * q.y + q.x * q.z), 1 - 2 * (q.y * q.y + q.x * q.x))
        let pitch = asin(max(-1, min(1, 2 * (q.w * q.x - q.z * q.y))))
        let updated = CameraPose(x: translation.x, y: translation.y, z: translation.z, yaw: yaw, pitch: pitch)
        pose = updated
        DispatchQueue.main.async { [weak self] in self?.onPose?(updated) }
    }

    func session(_ session: ARSession, didFailWithError error: Error) {
        let reason = "AR tracking failed: \(error.localizedDescription)"
        DispatchQueue.main.async { [weak self] in self?.onFailure?(reason) }
    }
}
