import AppKit
import Foundation

public final class WallpaperViewModel {
    public let configService: ConfigService
    public let desktopService: DesktopWindowService
    public let workspaceObserver: WorkspaceObserverService
    public let powerMonitor: PowerMonitorService

    public private(set) var config: AppConfig
    public private(set) var state: WallpaperState = WallpaperState()

    public var onStateChanged: ((WallpaperState) -> Void)?

    public init() {
        self.configService = ConfigService()
        self.config = configService.load()
        self.desktopService = DesktopWindowService()
        self.workspaceObserver = WorkspaceObserverService()
        self.powerMonitor = PowerMonitorService()

        setupListeners()

        if config.videoPath == nil || !FileManager.default.fileExists(atPath: config.videoPath!) {
            if let bundledSample = Bundle.main.path(forResource: "sakurajima_mai_live", ofType: "mp4", inDirectory: "sample-wallpapers") ??
                                   Bundle.main.path(forResource: "gojo_satoru_live", ofType: "mp4", inDirectory: "sample-wallpapers") {
                config.videoPath = bundledSample
            }
        }

        desktopService.configure(with: config)

        let activeDisplayPath = effectiveActiveVideoPath(for: config)
        if let path = activeDisplayPath {
            state.currentVideoPath = path
            state.isPlaying = true
        }
    }

    private func effectiveActiveVideoPath(for cfg: AppConfig) -> String? {
        if let path = cfg.videoPath, FileManager.default.fileExists(atPath: path) {
            return path
        }
        if cfg.multiMonitorMode == "PerMonitor" {
            return cfg.monitorVideos.values.first { FileManager.default.fileExists(atPath: $0) }
        }
        return nil
    }

    private func setupListeners() {
        workspaceObserver.onScreenSleepChanged = { [weak self] isSleeping in
            guard let self = self else { return }
            self.state.isPausedByScreenSleep = isSleeping
            self.evaluatePlayback()
        }

        workspaceObserver.onFullscreenChanged = { [weak self] isFullscreen in
            guard let self = self else { return }
            if self.config.autoPauseOnFullscreen {
                self.state.isPausedByFullscreen = isFullscreen
                self.evaluatePlayback()
            }
        }

        powerMonitor.onPowerStateChanged = { [weak self] isLowPower in
            guard let self = self else { return }
            if self.config.autoPauseOnBattery {
                self.state.isPausedByPower = isLowPower
                self.evaluatePlayback()
            }
        }
    }

    public func selectVideo(path: String) {
        guard FileManager.default.fileExists(atPath: path) else { return }
        config.videoPath = path
        configService.save(config)

        state.currentVideoPath = path
        state.isPausedByUser = false
        desktopService.loadVideo(path: path)
        evaluatePlayback()
    }

    public func togglePlayPause() {
        state.isPausedByUser = !state.isPausedByUser
        evaluatePlayback()
    }

    public func toggleMute() {
        config.isMuted = !config.isMuted
        configService.save(config)
        desktopService.setMuted(config.isMuted)
        notifyState()
    }

    public func updateConfig(_ newConfig: AppConfig) {
        let videoChanged = newConfig.videoPath != config.videoPath
        let multiMonitorChanged = newConfig.multiMonitorMode != config.multiMonitorMode
        let monitorVideosChanged = newConfig.monitorVideos != config.monitorVideos
        let scalingChanged = newConfig.scalingMode != config.scalingMode

        self.config = newConfig
        configService.save(newConfig)

        if multiMonitorChanged || monitorVideosChanged || videoChanged {
            state.currentVideoPath = effectiveActiveVideoPath(for: newConfig)
            state.isPausedByUser = false
            desktopService.configure(with: newConfig)
        } else {
            desktopService.setMuted(newConfig.isMuted)
            desktopService.setVolume(Float(newConfig.volume))
            if scalingChanged {
                desktopService.setScalingMode(newConfig.scalingMode)
            }
        }

        evaluatePlayback()
    }

    private func evaluatePlayback() {
        if state.shouldBePlaying {
            desktopService.play()
            state.isPlaying = true
        } else {
            desktopService.pause()
            state.isPlaying = false
        }
        notifyState()
    }

    private func notifyState() {
        onStateChanged?(state)
    }
}
