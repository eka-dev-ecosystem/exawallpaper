import AppKit
import SwiftUI
import UniformTypeIdentifiers

public final class MenuBarView: NSObject {
    private let viewModel: WallpaperViewModel
    private var statusItem: NSStatusItem!
    private var settingsWindow: NSWindow?

    private var playPauseItem: NSMenuItem!
    private var muteItem: NSMenuItem!
    private var statusTitleItem: NSMenuItem!

    public init(viewModel: WallpaperViewModel) {
        self.viewModel = viewModel
        super.init()

        setupStatusItem()
        updateMenuItems()

        viewModel.onStateChanged = { [weak self] _ in
            self?.updateMenuItems()
        }
    }

    private func setupStatusItem() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)

        if let button = statusItem.button {
            if let image = NSImage(systemSymbolName: "play.tv", accessibilityDescription: "Live Wallpaper") {
                image.isTemplate = true
                button.image = image
            } else {
                button.title = "▶"
            }
        }

        let menu = NSMenu()

        statusTitleItem = NSMenuItem(title: "Live Wallpaper", action: nil, keyEquivalent: "")
        statusTitleItem.isEnabled = false
        menu.addItem(statusTitleItem)

        menu.addItem(NSMenuItem.separator())

        playPauseItem = NSMenuItem(title: "Pause", action: #selector(togglePlayPause), keyEquivalent: "p")
        playPauseItem.target = self
        menu.addItem(playPauseItem)

        muteItem = NSMenuItem(title: "Mute", action: #selector(toggleMute), keyEquivalent: "m")
        muteItem.target = self
        menu.addItem(muteItem)

        let chooseItem = NSMenuItem(title: "Choose Video File...", action: #selector(chooseVideo), keyEquivalent: "o")
        chooseItem.target = self
        menu.addItem(chooseItem)

        menu.addItem(NSMenuItem.separator())

        let settingsItem = NSMenuItem(title: "Settings...", action: #selector(openSettings), keyEquivalent: ",")
        settingsItem.target = self
        menu.addItem(settingsItem)

        menu.addItem(NSMenuItem.separator())

        let quitItem = NSMenuItem(title: "Quit Live Wallpaper", action: #selector(quitApp), keyEquivalent: "q")
        quitItem.target = self
        menu.addItem(quitItem)

        statusItem.menu = menu
    }

    public func updateMenuItems() {
        let state = viewModel.state
        let config = viewModel.config

        if let path = state.currentVideoPath {
            let filename = (path as NSString).lastPathComponent
            statusTitleItem.title = "Video: \(filename)"
        } else {
            statusTitleItem.title = "No Video Selected"
        }

        playPauseItem.title = state.isPlaying ? "Pause" : "Play"
        muteItem.title = config.isMuted ? "Unmute Audio" : "Mute Audio"
    }

    @objc private func togglePlayPause() {
        viewModel.togglePlayPause()
    }

    @objc private func toggleMute() {
        viewModel.toggleMute()
    }

    @objc private func chooseVideo() {
        let panel = NSOpenPanel()
        panel.canChooseFiles = true
        panel.canChooseDirectories = false
        panel.allowsMultipleSelection = false
        panel.allowedContentTypes = [.movie, .video, .quickTimeMovie, .mpeg4Movie]

        if panel.runModal() == .OK, let url = panel.url {
            viewModel.selectVideo(path: url.path)
        }
    }

    @objc public func openSettings() {
        if let existing = settingsWindow {
            existing.makeKeyAndOrderFront(nil)
            NSApp.activate(ignoringOtherApps: true)
            return
        }

        let settingsView = SettingsView(
            config: viewModel.config,
            onSave: { [weak self] newConfig in
                self?.viewModel.updateConfig(newConfig)
                self?.settingsWindow?.close()
                self?.settingsWindow = nil
            },
            onCancel: { [weak self] in
                self?.settingsWindow?.close()
                self?.settingsWindow = nil
            }
        )

        let hostingController = NSHostingController(rootView: settingsView)
        let window = NSWindow(contentViewController: hostingController)
        window.title = "Live Wallpaper Settings"
        window.styleMask = [.titled, .closable]
        window.center()
        window.isReleasedWhenClosed = false

        self.settingsWindow = window
        window.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    @objc private func quitApp() {
        viewModel.desktopService.cleanup()
        NSApplication.shared.terminate(nil)
    }
}
