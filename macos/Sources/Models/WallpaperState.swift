import Foundation

public struct WallpaperState {
    public var isPlaying: Bool = false
    public var currentVideoPath: String? = nil
    public var isPausedByUser: Bool = false
    public var isPausedByFullscreen: Bool = false
    public var isPausedByPower: Bool = false
    public var isPausedByScreenSleep: Bool = false

    public var shouldBePlaying: Bool {
        return currentVideoPath != nil
            && !isPausedByUser
            && !isPausedByFullscreen
            && !isPausedByPower
            && !isPausedByScreenSleep
    }
}
