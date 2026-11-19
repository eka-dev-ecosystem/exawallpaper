import AppKit
import AVFoundation
import Foundation

final class PlayerHostingView: NSView {
    let playerLayer: AVPlayerLayer

    init(frame: NSRect, playerLayer: AVPlayerLayer) {
        self.playerLayer = playerLayer
        super.init(frame: frame)
        self.wantsLayer = true
        self.layer?.backgroundColor = NSColor.black.cgColor
        self.layer?.addSublayer(playerLayer)
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    override func layout() {
        super.layout()
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        playerLayer.frame = bounds
        CATransaction.commit()
    }
}

public final class DesktopWindowService {
    private struct DisplayInstance {
        let window: NSWindow
        let hostingView: PlayerHostingView
        let playerService: VideoPlayerService
    }

    private var instances: [DisplayInstance] = []
    private var screenObserver: NSObjectProtocol?

    public init() {
        screenObserver = NotificationCenter.default.addObserver(
            forName: NSApplication.didChangeScreenParametersNotification,
            object: nil,
            queue: .main
        ) { [weak self] _ in
            self?.reconfigureWindows()
        }
    }

    deinit {
        if let observer = screenObserver {
            NotificationCenter.default.removeObserver(observer)
        }
        cleanup()
    }

    private var currentConfig: AppConfig = .default

    public func configure(with config: AppConfig) {
        self.currentConfig = config
        reconfigureWindows()
    }

    public func play() {
        instances.forEach { $0.playerService.play() }
    }

    public func pause() {
        instances.forEach { $0.playerService.pause() }
    }

    public func setMuted(_ isMuted: Bool) {
        instances.forEach { $0.playerService.setMuted(isMuted) }
    }

    public func setVolume(_ volume: Float) {
        instances.forEach { $0.playerService.setVolume(volume) }
    }

    public func setScalingMode(_ mode: String) {
        instances.forEach { $0.playerService.setScalingMode(mode) }
    }

    private func resolveURL(from path: String) -> URL? {
        if path.hasPrefix("file://") {
            return URL(string: path)
        }
        return URL(fileURLWithPath: path)
    }

    public func loadVideo(path: String) {
        currentConfig.videoPath = path
        reconfigureWindows()
    }

    private func reconfigureWindows() {
        cleanup()

        let screens = NSScreen.screens
        guard !screens.isEmpty else { return }

        switch currentConfig.multiMonitorMode {
        case "Span":
            // "ke 3 monitor jadi satu" - Span single window across bounding frame of all screens
            var unionFrame = screens[0].frame
            for s in screens.dropFirst() {
                unionFrame = unionFrame.union(s.frame)
            }
            let targetScreen = NSScreen.main ?? screens[0]
            let instance = createDesktopWindow(frame: unionFrame, screen: targetScreen)
            instances.append(instance)

            if let path = currentConfig.videoPath, FileManager.default.fileExists(atPath: path),
               let url = resolveURL(from: path) {
                instance.playerService.loadVideo(
                    url: url,
                    isMuted: currentConfig.isMuted,
                    volume: Float(currentConfig.volume),
                    scalingMode: currentConfig.scalingMode
                )
            }

        case "PerMonitor":
            // "setiap monitor bisa beda beda" - Separate windows playing monitor-specific videos
            for (index, screen) in screens.enumerated() {
                let instance = createDesktopWindow(frame: screen.frame, screen: screen)
                instances.append(instance)

                let selectedPath = currentConfig.monitorVideos[String(index)] ?? currentConfig.videoPath
                if let path = selectedPath, FileManager.default.fileExists(atPath: path),
                   let url = resolveURL(from: path) {
                    instance.playerService.loadVideo(
                        url: url,
                        isMuted: currentConfig.isMuted,
                        volume: Float(currentConfig.volume),
                        scalingMode: currentConfig.scalingMode
                    )
                }
            }

        case "PrimaryOnly":
            let screen = NSScreen.main ?? screens[0]
            let instance = createDesktopWindow(frame: screen.frame, screen: screen)
            instances.append(instance)

            if let path = currentConfig.videoPath, FileManager.default.fileExists(atPath: path),
               let url = resolveURL(from: path) {
                instance.playerService.loadVideo(
                    url: url,
                    isMuted: currentConfig.isMuted,
                    volume: Float(currentConfig.volume),
                    scalingMode: currentConfig.scalingMode
                )
            }

        default: // "Duplicate" or legacy "AllDisplays"
            for screen in screens {
                let instance = createDesktopWindow(frame: screen.frame, screen: screen)
                instances.append(instance)

                if let path = currentConfig.videoPath, FileManager.default.fileExists(atPath: path),
                   let url = resolveURL(from: path) {
                    instance.playerService.loadVideo(
                        url: url,
                        isMuted: currentConfig.isMuted,
                        volume: Float(currentConfig.volume),
                        scalingMode: currentConfig.scalingMode
                    )
                }
            }
        }
    }

    private func createDesktopWindow(frame: NSRect, screen: NSScreen) -> DisplayInstance {
        let window = NSWindow(
            contentRect: frame,
            styleMask: [.borderless],
            backing: .buffered,
            defer: false,
            screen: screen
        )

        // Position window directly at the desktop level behind desktop icons
        window.level = NSWindow.Level(Int(CGWindowLevelForKey(.desktopWindow)))
        window.canHide = false
        window.hidesOnDeactivate = false
        window.isOpaque = false
        window.backgroundColor = .black

        // Crucial for native desktop: click through to desktop icons
        window.ignoresMouseEvents = true

        // Stay on all virtual desktops / spaces and ignore window cycling
        window.collectionBehavior = [.canJoinAllSpaces, .stationary, .ignoresCycle]

        let playerService = VideoPlayerService()
        let hostingView = PlayerHostingView(frame: NSRect(origin: .zero, size: frame.size), playerLayer: playerService.playerLayer)
        window.contentView = hostingView

        window.orderFront(nil)

        return DisplayInstance(window: window, hostingView: hostingView, playerService: playerService)
    }

    public func cleanup() {
        for instance in instances {
            instance.playerService.pause()
            instance.window.orderOut(nil)
        }
        instances.removeAll()
    }
}
