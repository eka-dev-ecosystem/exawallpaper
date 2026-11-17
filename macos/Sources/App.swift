import AppKit

@main
final class AppDelegate: NSObject, NSApplicationDelegate {
    private var viewModel: WallpaperViewModel!
    private var menuBarView: MenuBarView!

    static func main() {
        let app = NSApplication.shared
        let delegate = AppDelegate()
        app.delegate = delegate
        _ = NSApplicationMain(CommandLine.argc, CommandLine.unsafeArgv)
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        // Run as pure Menu Bar accessory app (no Dock icon)
        NSApp.setActivationPolicy(.accessory)

        self.viewModel = WallpaperViewModel()
        self.menuBarView = MenuBarView(viewModel: viewModel)

        // Show settings on first run if no video is selected
        if viewModel.config.videoPath == nil {
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) { [weak self] in
                self?.menuBarView.openSettings()
            }
        }
    }

    func applicationWillTerminate(_ notification: Notification) {
        viewModel?.desktopService.cleanup()
    }
}
