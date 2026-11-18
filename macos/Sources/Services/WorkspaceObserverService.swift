import AppKit
import CoreGraphics
import Foundation

public final class WorkspaceObserverService {
    public var onScreenSleepChanged: ((Bool) -> Void)?
    public var onFullscreenChanged: ((Bool) -> Void)?

    private var observers: [NSObjectProtocol] = []
    private var distObservers: [NSObjectProtocol] = []
    private var pollingTimer: Timer?
    private var lastFullscreenState = false

    public init() {
        registerWorkspaceNotifications()
        startPolling()
    }

    deinit {
        stop()
    }

    public func startPolling() {
        pollingTimer?.invalidate()
        pollingTimer = Timer.scheduledTimer(withTimeInterval: 1.0, repeats: true) { [weak self] _ in
            self?.checkFullscreen()
        }
    }

    public func stop() {
        pollingTimer?.invalidate()
        pollingTimer = nil

        let nc = NSWorkspace.shared.notificationCenter
        observers.forEach { nc.removeObserver($0) }
        observers.removeAll()

        let dnc = DistributedNotificationCenter.default()
        distObservers.forEach { dnc.removeObserver($0) }
        distObservers.removeAll()
    }

    private func registerWorkspaceNotifications() {
        let wsCenter = NSWorkspace.shared.notificationCenter

        // Screen sleep
        observers.append(
            wsCenter.addObserver(
                forName: NSWorkspace.screensDidSleepNotification,
                object: nil,
                queue: .main
            ) { [weak self] _ in
                self?.onScreenSleepChanged?(true)
            }
        )

        // Screen wake
        observers.append(
            wsCenter.addObserver(
                forName: NSWorkspace.screensDidWakeNotification,
                object: nil,
                queue: .main
            ) { [weak self] _ in
                self?.onScreenSleepChanged?(false)
                self?.checkFullscreen()
            }
        )

        // Active Space changed (switching desktops or fullscreen spaces)
        observers.append(
            wsCenter.addObserver(
                forName: NSWorkspace.activeSpaceDidChangeNotification,
                object: nil,
                queue: .main
            ) { [weak self] _ in
                // Give OS a moment to settle window layout
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.3) {
                    self?.checkFullscreen()
                }
            }
        )

        // Screen lock/unlock via DistributedNotificationCenter
        let distCenter = DistributedNotificationCenter.default()
        distObservers.append(
            distCenter.addObserver(
                forName: NSNotification.Name("com.apple.screenIsLocked"),
                object: nil,
                queue: .main
            ) { [weak self] _ in
                self?.onScreenSleepChanged?(true)
            }
        )

        distObservers.append(
            distCenter.addObserver(
                forName: NSNotification.Name("com.apple.screenIsUnlocked"),
                object: nil,
                queue: .main
            ) { [weak self] _ in
                self?.onScreenSleepChanged?(false)
                self?.checkFullscreen()
            }
        )
    }

    public func checkFullscreen() {
        guard let screen = NSScreen.main else { return }
        let screenFrame = screen.frame

        guard let windowInfoList = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID) as? [[String: Any]] else {
            updateFullscreen(false)
            return
        }

        var isCovered = false

        for info in windowInfoList {
            // Layer 0 is standard application window layer
            guard let layer = info[kCGWindowLayer as String] as? Int, layer == 0 else { continue }
            guard let ownerName = info[kCGWindowOwnerName as String] as? String else { continue }
            guard let alpha = info[kCGWindowAlpha as String] as? Double, alpha > 0.8 else { continue }

            // Skip desktop elements, dock, our own wallpaper app
            if ownerName == "Dock" || ownerName == "Window Server" || ownerName == "ExaWallpaper" || ownerName == "Finder" {
                continue
            }

            if let boundsDict = info[kCGWindowBounds as String] as? [String: Any],
               let bounds = CGRect(dictionaryRepresentation: boundsDict as CFDictionary) {
                // If a normal app window covers approximately the whole screen (allowing 1-2px rounding)
                if bounds.width >= (screenFrame.width - 2) && bounds.height >= (screenFrame.height - 2) {
                    isCovered = true
                    break
                }
            }
        }

        updateFullscreen(isCovered)
    }

    private func updateFullscreen(_ isFullscreen: Bool) {
        if isFullscreen != lastFullscreenState {
            lastFullscreenState = isFullscreen
            onFullscreenChanged?(isFullscreen)
        }
    }
}
