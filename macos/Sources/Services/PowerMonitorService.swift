import Foundation

public final class PowerMonitorService {
    public var onPowerStateChanged: ((Bool) -> Void)?
    private var observer: NSObjectProtocol?

    public var isLowPowerModeEnabled: Bool {
        return ProcessInfo.processInfo.isLowPowerModeEnabled
    }

    public init() {
        start()
    }

    deinit {
        stop()
    }

    public func start() {
        if observer == nil {
            observer = NotificationCenter.default.addObserver(
                forName: NSNotification.Name.NSProcessInfoPowerStateDidChange,
                object: nil,
                queue: .main
            ) { [weak self] _ in
                guard let self = self else { return }
                let isLowPower = ProcessInfo.processInfo.isLowPowerModeEnabled
                self.onPowerStateChanged?(isLowPower)
            }
        }
    }

    public func stop() {
        if let observer = observer {
            NotificationCenter.default.removeObserver(observer)
            self.observer = nil
        }
    }
}
