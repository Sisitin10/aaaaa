import Foundation
import UIKit

struct CameraPose {
    var x: Float = 0
    var y: Float = 0
    var z: Float = 0
    var yaw: Float = 0
    var pitch: Float = 0
}

enum CameraTab: Hashable {
    case shoot
    case setup
    case settings
}
