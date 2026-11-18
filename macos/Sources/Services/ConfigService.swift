import Foundation

public final class ConfigService {
    private let fileURL: URL
    private let encoder: JSONEncoder
    private let decoder: JSONDecoder

    public init() {
        let appSupport = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first!
        let folderURL = appSupport.appendingPathComponent("ExaWallpaper", isDirectory: true)

        try? FileManager.default.createDirectory(at: folderURL, withIntermediateDirectories: true)
        self.fileURL = folderURL.appendingPathComponent("config.json")

        self.encoder = JSONEncoder()
        self.encoder.outputFormatting = .prettyPrinted
        self.decoder = JSONDecoder()
    }

    public func load() -> AppConfig {
        guard FileManager.default.fileExists(atPath: fileURL.path) else {
            let initial = AppConfig.default
            save(initial)
            return initial
        }

        do {
            let data = try Data(contentsOf: fileURL)
            return try decoder.decode(AppConfig.self, from: data)
        } catch {
            print("[ConfigService] Failed to load config: \(error.localizedDescription)")
            return AppConfig.default
        }
    }

    public func save(_ config: AppConfig) {
        do {
            let data = try encoder.encode(config)
            try data.write(to: fileURL, options: .atomic)
        } catch {
            print("[ConfigService] Failed to save config: \(error.localizedDescription)")
        }
    }
}
