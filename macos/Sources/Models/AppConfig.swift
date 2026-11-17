import Foundation

public struct AppConfig: Codable, Equatable {
    public var videoPath: String?
    public var isMuted: Bool
    public var volume: Double
    public var scalingMode: String // "AspectFill", "AspectFit", "Stretch"
    public var autoPauseOnFullscreen: Bool
    public var autoPauseOnBattery: Bool
    public var multiMonitorMode: String // "AllDisplays", "PrimaryOnly"
    public var runAtStartup: Bool

    public init(
        videoPath: String? = nil,
        isMuted: Bool = true,
        volume: Double = 0.5,
        scalingMode: String = "AspectFill",
        autoPauseOnFullscreen: Bool = true,
        autoPauseOnBattery: Bool = true,
        multiMonitorMode: String = "AllDisplays",
        runAtStartup: Bool = false
    ) {
        self.videoPath = videoPath
        self.isMuted = isMuted
        self.volume = volume
        self.scalingMode = scalingMode
        self.autoPauseOnFullscreen = autoPauseOnFullscreen
        self.autoPauseOnBattery = autoPauseOnBattery
        self.multiMonitorMode = multiMonitorMode
        self.runAtStartup = runAtStartup
    }

    public static let `default` = AppConfig()
}
