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
    @State private var monitorVideos: [String: String]
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
        self._monitorVideos = State(initialValue: config.monitorVideos)
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
                    GroupBox(label: Label("Default Video Source", systemImage: "film")) {
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
                                    chooseVideoFile { path in
                                        self.videoPath = path
                                    }
                                }
                                .buttonStyle(.borderedProminent)
                            }
                            Text("Supported formats: MP4 (H.264/HEVC), MOV, WebM")
                                .font(.caption)
                                .foregroundColor(.secondary)
                        }
                        .padding(6)
                    }

                    // Multi-Monitor Setup
                    GroupBox(label: Label("Multi-Monitor Display Setup", systemImage: "display.2")) {
                        VStack(alignment: .leading, spacing: 10) {
                            Picker("Display Mode:", selection: $multiMonitorMode) {
                                Text("Span All Displays (Semua Monitor Jadi Satu)").tag("Span")
                                Text("Different Video Per Monitor (Tiap Monitor Beda)").tag("PerMonitor")
                                Text("Duplicate on All Displays (Semua Monitor Sama)").tag("Duplicate")
                                Text("Primary Display Only").tag("PrimaryOnly")
                            }

                            if multiMonitorMode == "PerMonitor" {
                                Divider()
                                Text("Custom Video Per Monitor:")
                                    .font(.caption)
                                    .fontWeight(.semibold)
                                    .foregroundColor(.blue)

                                ForEach(Array(NSScreen.screens.enumerated()), id: \.offset) { index, screen in
                                    let key = String(index)
                                    let path = monitorVideos[key] ?? ""
                                    VStack(alignment: .leading, spacing: 4) {
                                        Text("Monitor \(index + 1): \(screen.localizedName)\(index == 0 ? " (Primary)" : "") (\(Int(screen.frame.width))x\(Int(screen.frame.height)))")
                                            .font(.caption)
                                            .foregroundColor(.secondary)

                                        HStack {
                                            Text(path.isEmpty ? (videoPath.isEmpty ? "Using default" : (videoPath as NSString).lastPathComponent) : (path as NSString).lastPathComponent)
                                                .font(.caption)
                                                .lineLimit(1)
                                                .truncationMode(.middle)
                                                .foregroundColor(path.isEmpty ? .secondary : .primary)
                                                .frame(maxWidth: .infinity, alignment: .leading)
                                                .padding(4)
                                                .background(Color(NSColor.textBackgroundColor))
                                                .cornerRadius(4)

                                            if !path.isEmpty {
                                                Button("Reset") {
                                                    monitorVideos.removeValue(forKey: key)
                                                }
                                                .buttonStyle(.bordered)
                                                .controlSize(.small)
                                            }

                                            Button("Browse...") {
                                                chooseVideoFile { selected in
                                                    monitorVideos[key] = selected
                                                }
                                            }
                                            .buttonStyle(.bordered)
                                            .controlSize(.small)
                                        }
                                    }
                                    .padding(.vertical, 2)
                                }
                            }
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

                    // Display Scaling
                    GroupBox(label: Label("Video Scaling", systemImage: "aspectratio")) {
                        VStack(alignment: .leading, spacing: 10) {
                            Picker("Scaling Mode:", selection: $scalingMode) {
                                Text("Aspect Fill (Crop to Fill)").tag("AspectFill")
                                Text("Aspect Fit (Letterbox)").tag("AspectFit")
                                Text("Stretch (Distort to Fill)").tag("Stretch")
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
                    newConfig.monitorVideos = monitorVideos
                    newConfig.runAtStartup = runAtStartup
                    onSave(newConfig)
                }
                .buttonStyle(.borderedProminent)
                .keyboardShortcut(.defaultAction)
            }
        }
        .padding(20)
        .frame(width: 500, height: 620)
    }

    private func chooseVideoFile(completion: @escaping (String) -> Void) {
        let panel = NSOpenPanel()
        panel.canChooseFiles = true
        panel.canChooseDirectories = false
        panel.allowsMultipleSelection = false
        panel.allowedContentTypes = [.movie, .video, .quickTimeMovie, .mpeg4Movie]

        if panel.runModal() == .OK, let url = panel.url {
            completion(url.path)
        }
    }
}
