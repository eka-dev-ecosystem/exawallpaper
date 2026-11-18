import AppKit
import SwiftUI

public struct SettingsView: View {
    @State private var videoPath: String
    @State private var isMuted: Bool
    @State private var volume: Double
    @State private var scalingMode: String
    @State private var autoPauseFullscreen: Bool
    @State private var autoPauseBattery: Bool
    @State private var multiMonitorMode: String
    @State private var runAtStartup: Bool

    private let onSave: (AppConfig) -> Void
    private let onCancel: () -> Void

    public init(config: AppConfig, onSave: @escaping (AppConfig) -> Void, onCancel: @escaping () -> Void) {
        self._videoPath = State(initialValue: config.videoPath ?? "")
        self._isMuted = State(initialValue: config.isMuted)
        self._volume = State(initialValue: config.volume * 100)
        self._scalingMode = State(initialValue: config.scalingMode)
        self._autoPauseFullscreen = State(initialValue: config.autoPauseOnFullscreen)
        self._autoPauseBattery = State(initialValue: config.autoPauseOnBattery)
        self._multiMonitorMode = State(initialValue: config.multiMonitorMode)
        self._runAtStartup = State(initialValue: StartupService.isEnabled)
        self.onSave = onSave
        self.onCancel = onCancel
    }

    public var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            // Header
            HStack {
                VStack(alignment: .leading, spacing: 2) {
                    Text("ExaWallpaper Settings")
                        .font(.title2)
                        .fontWeight(.bold)
                    Text("Ultra-lightweight native video wallpaper")
                        .font(.subheadline)
                        .foregroundColor(.secondary)
                }
                Spacer()
            }

            Divider()

            ScrollView {
                VStack(alignment: .leading, spacing: 16) {
                    // Video File Section
                    GroupBox(label: Label("Video Source", systemImage: "film")) {
                        VStack(alignment: .leading, spacing: 8) {
                            HStack {
                                Text(videoPath.isEmpty ? "No video selected" : (videoPath as NSString).lastPathComponent)
                                    .lineLimit(1)
                                    .truncationMode(.middle)
                                    .foregroundColor(videoPath.isEmpty ? .secondary : .primary)
                                    .frame(maxWidth: .infinity, alignment: .leading)
                                    .padding(6)
                                    .background(Color(NSColor.textBackgroundColor))
                                    .cornerRadius(6)

                                Button("Browse...") {
                                    chooseVideoFile()
                                }
                                .buttonStyle(.borderedProminent)
                            }
                            Text("Supported formats: MP4 (H.264/HEVC), MOV, WebM")
                                .font(.caption)
                                .foregroundColor(.secondary)
                        }
                        .padding(6)
                    }

                    // Audio Settings
                    GroupBox(label: Label("Audio Settings", systemImage: "speaker.wave.2")) {
                        VStack(alignment: .leading, spacing: 10) {
                            Toggle("Mute audio (recommended)", isOn: $isMuted)

                            if !isMuted {
                                HStack {
                                    Text("Volume")
                                    Slider(value: $volume, in: 0...100, step: 1)
                                    Text("\(Int(volume))%")
                                        .frame(width: 40, alignment: .trailing)
                                }
                            }
                        }
                        .padding(6)
                    }

                    // Performance & Power Saving
                    GroupBox(label: Label("Performance & Battery Saving", systemImage: "bolt.batteryblock")) {
                        VStack(alignment: .leading, spacing: 10) {
                            Toggle("Auto-pause when full-screen app is active", isOn: $autoPauseFullscreen)
                            Toggle("Auto-pause in Low Power Mode", isOn: $autoPauseBattery)
                        }
                        .padding(6)
                    }

                    // Display & Scaling
                    GroupBox(label: Label("Display & Scaling", systemImage: "display")) {
                        VStack(alignment: .leading, spacing: 10) {
                            Picker("Scaling Mode:", selection: $scalingMode) {
                                Text("Aspect Fill (Crop to Fill)").tag("AspectFill")
                                Text("Aspect Fit (Letterbox)").tag("AspectFit")
                                Text("Stretch (Distort to Fill)").tag("Stretch")
                            }

                            Picker("Displays:", selection: $multiMonitorMode) {
                                Text("All Connected Displays").tag("AllDisplays")
                                Text("Primary Display Only").tag("PrimaryOnly")
                            }
                        }
                        .padding(6)
                    }

                    // System Options
                    GroupBox(label: Label("System Options", systemImage: "gearshape")) {
                        VStack(alignment: .leading, spacing: 10) {
                            Toggle("Start automatically at login", isOn: $runAtStartup)
                        }
                        .padding(6)
                    }
                }
            }

            Divider()

            // Footer
            HStack {
                Spacer()
                Button("Cancel") {
                    onCancel()
                }
                .keyboardShortcut(.cancelAction)

                Button("Save & Apply") {
                    StartupService.setEnabled(runAtStartup)
                    var newConfig = AppConfig()
                    newConfig.videoPath = videoPath.isEmpty ? nil : videoPath
                    newConfig.isMuted = isMuted
                    newConfig.volume = volume / 100.0
                    newConfig.scalingMode = scalingMode
                    newConfig.autoPauseOnFullscreen = autoPauseFullscreen
                    newConfig.autoPauseOnBattery = autoPauseBattery
                    newConfig.multiMonitorMode = multiMonitorMode
                    newConfig.runAtStartup = runAtStartup
                    onSave(newConfig)
                }
                .buttonStyle(.borderedProminent)
                .keyboardShortcut(.defaultAction)
            }
        }
        .padding(20)
        .frame(width: 480, height: 560)
    }

    private func chooseVideoFile() {
        let panel = NSOpenPanel()
        panel.canChooseFiles = true
        panel.canChooseDirectories = false
        panel.allowsMultipleSelection = false
        panel.allowedContentTypes = [.movie, .video, .quickTimeMovie, .mpeg4Movie]

        if panel.runModal() == .OK, let url = panel.url {
            videoPath = url.path
        }
    }
}
