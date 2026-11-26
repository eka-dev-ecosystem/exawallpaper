import AppKit
import AVFoundation
import Foundation

final class PlayerHostingView: NSView {
    let playerLayer: AVPlayerLayer
    private let spanLayerFrame: NSRect?

    init(frame: NSRect, playerLayer: AVPlayerLayer, spanLayerFrame: NSRect? = nil) {
        self.playerLayer = playerLayer
        self.spanLayerFrame = spanLayerFrame
        super.init(frame: frame)
        self.wantsLayer = true
        self.layer?.backgroundColor = NSColor.black.cgColor
        self.layer?.masksToBounds = true
        self.layer?.addSublayer(playerLayer)
        updateLayerFrame()
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    override func layout() {
        super.layout()
        updateLayerFrame()
    }

    override func viewDidChangeBackingProperties() {
        super.viewDidChangeBackingProperties()
        if let scale = window?.backingScaleFactor {
            playerLayer.contentsScale = scale
        }
    }

    private func updateLayerFrame() {
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        playerLayer.frame = spanLayerFrame ?? bounds
        if let scale = window?.backingScaleFactor {
            playerLayer.contentsScale = scale
        }
        CATransaction.commit()
    }
}

public final class DesktopWindowService {
    private var windows: [NSWindow] = []
    private var hostingViews: [PlayerHostingView] = []
    private var playerServices: [VideoPlayerService] = []
    private var screenObserver: NSObjectProtocol?
    private var reconfigureWorkItem: DispatchWorkItem?

    public init() {
        screenObserver = NotificationCenter.default.addObserver(
            forName: NSApplication.didChangeScreenParametersNotification,
            object: nil,
            queue: .main
        ) { [weak self] _ in
            self?.scheduleReconfigure()
        }
    }

    deinit {
        reconfigureWorkItem?.cancel()
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
        playerServices.forEach { $0.play() }
    }

    public func pause() {
        playerServices.forEach { $0.pause() }
    }

    public func setMuted(_ isMuted: Bool) {
        playerServices.forEach { $0.setMuted(isMuted) }
    }

    public func setVolume(_ volume: Float) {
        playerServices.forEach { $0.setVolume(volume) }
    }

    public func setScalingMode(_ mode: String) {
        playerServices.forEach { $0.setScalingMode(mode) }
    }

    private func resolveURL(from path: String?) -> URL? {
        guard let path = path, !path.isEmpty, FileManager.default.fileExists(atPath: path) else {
            return nil
        }
        if path.hasPrefix("file://") {
            return URL(string: path)
        }
        return URL(fileURLWithPath: path)
    }

    public func loadVideo(path: String) {
        currentConfig.videoPath = path
        reconfigureWindows()
    }

    private func scheduleReconfigure() {
        reconfigureWorkItem?.cancel()
        let item = DispatchWorkItem { [weak self] in
            self?.reconfigureWindows()
        }
        reconfigureWorkItem = item
        // Briefly coalesce rapid display-change bursts while monitors settle.
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.15, execute: item)
    }

    private func reconfigureWindows() {
        cleanup()

        let screens = NSScreen.screens
        guard !screens.isEmpty else { return }

        switch currentConfig.multiMonitorMode {
        case "Span":
            // Render a synchronized video sliced across each screen's window so
            // spanning works even with "Displays have separate Spaces" and mixed DPI.
            let unionFrame = screens.dropFirst().reduce(screens[0].frame) { $0.union($1.frame) }
            let sharedService = VideoPlayerService()
            playerServices.append(sharedService)

            for (index, screen) in screens.enumerated() {
                let layer = index == 0 ? sharedService.playerLayer : sharedService.makeLayer()
                let spanLayerFrame = NSRect(
                    x: unionFrame.minX - screen.frame.minX,
                    y: unionFrame.minY - screen.frame.minY,
                    width: unionFrame.width,
                    height: unionFrame.height
                )
                createDesktopWindow(for: screen, playerLayer: layer, spanLayerFrame: spanLayerFrame)
            }

            loadVideo(on: sharedService, url: resolveURL(from: currentConfig.videoPath))

        case "PerMonitor":
            for (index, screen) in screens.enumerated() {
                let service = VideoPlayerService()
                playerServices.append(service)
                createDesktopWindow(for: screen, playerLayer: service.playerLayer)

                let customURL = resolveURL(from: currentConfig.monitorVideos[String(index)])
                let targetURL = customURL ?? resolveURL(from: currentConfig.videoPath)
                loadVideo(on: service, url: targetURL)
            }

        case "PrimaryOnly":
            // NSScreen.screens[0] is always the primary display (menu bar display at (0,0)),
            // whereas NSScreen.main tracks whichever screen currently holds key focus.
            let primaryScreen = screens[0]
            let service = VideoPlayerService()
            playerServices.append(service)
            createDesktopWindow(for: primaryScreen, playerLayer: service.playerLayer)
            loadVideo(on: service, url: resolveURL(from: currentConfig.videoPath))

        default: // "Duplicate" or legacy "AllDisplays"
            let sharedService = VideoPlayerService()
            playerServices.append(sharedService)

            for (index, screen) in screens.enumerated() {
                let layer = index == 0 ? sharedService.playerLayer : sharedService.makeLayer()
                createDesktopWindow(for: screen, playerLayer: layer)
            }

            loadVideo(on: sharedService, url: resolveURL(from: currentConfig.videoPath))
        }
    }

    private func loadVideo(on service: VideoPlayerService, url: URL?) {
        guard let url = url else { return }
        service.loadVideo(
            url: url,
            isMuted: currentConfig.isMuted,
            volume: Float(currentConfig.volume),
            scalingMode: currentConfig.scalingMode
        )
    }

    private func createDesktopWindow(
        for screen: NSScreen,
        playerLayer: AVPlayerLayer,
        spanLayerFrame: NSRect? = nil
    ) {
        let frame = screen.frame
        // NSWindow(contentRect:..., screen:) treats contentRect as relative to screen.frame.origin.
        // Passing origin .zero prevents double-offsetting secondary monitors, and setFrame guarantees exact global bounds.
        let window = NSWindow(
            contentRect: NSRect(origin: .zero, size: frame.size),
            styleMask: [.borderless],
            backing: .buffered,
            defer: false,
            screen: screen
        )
        window.setFrame(frame, display: false)
        window.hasShadow = false

        // Position window directly at the desktop level behind desktop icons
        window.level = NSWindow.Level(Int(CGWindowLevelForKey(.desktopWindow)))
        window.canHide = false
        window.hidesOnDeactivate = false
        window.isOpaque = true
        window.backgroundColor = .black

        // Crucial for native desktop: click through to desktop icons
        window.ignoresMouseEvents = true

        // Stay on all virtual desktops / spaces and ignore window cycling
        window.collectionBehavior = [.canJoinAllSpaces, .stationary, .ignoresCycle]

        let hostingView = PlayerHostingView(
            frame: NSRect(origin: .zero, size: frame.size),
            playerLayer: playerLayer,
            spanLayerFrame: spanLayerFrame
        )
        window.contentView = hostingView

        window.orderFront(nil)

        windows.append(window)
        hostingViews.append(hostingView)
    }

    public func cleanup() {
        for service in playerServices {
            service.dispose()
        }
        playerServices.removeAll()

        for window in windows {
            window.orderOut(nil)
        }
        windows.removeAll()
        hostingViews.removeAll()
    }
}
