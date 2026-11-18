# 🎬 ExaWallpaper

> **Ultra-lightweight, 100% Native, Battery-Friendly Desktop Video Wallpaper Engine for macOS & Windows.**

[![Build & Validation](https://github.com/eka-dev-ecosystem/exawallpaper/actions/workflows/ci.yml/badge.svg)](https://github.com/eka-dev-ecosystem/exawallpaper/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-macOS%20%7C%20Windows-lightgrey.svg)]()

ExaWallpaper adalah aplikasi live video wallpaper desktop modern tanpa runtime berat (tanpa Electron, Chromium, atau WebView2). Berjalan murni secara native menggunakan API grafis sistem operasi:
- **macOS**: Swift 5.9+ / AppKit, `AVFoundation` (`AVQueuePlayer` + `AVPlayerLooper`), akselerasi penuh VideoToolbox/Metal, dan desktop injection pada level `kCGDesktopWindowLevel`.
- **Windows**: C# .NET 8 / Win32 API, injeksi jendela `WorkerW` tepat di belakang `SHELLDLL_DefView`, dan akselerasi hardware DirectX / Media Foundation.

---

## ⚡ Fitur Utama

- 🚀 **100% Native & Ultra-Lightweight**: Menggunakan memori RAM < 35 MB dan CPU usage mendekati 0% berkat pemanfaatan hardware video decoder GPU.
- 📂 **Local-First & Offline**: Putar video milik sendiri dari disk lokal (.mp4, .mov, .webm) tanpa koneksi internet atau API eksternal.
- 🔁 **Seamless Video Looping**: Menggunakan `AVPlayerLooper` di macOS dan direct timeline rewind di Windows tanpa frame hitam atau jeda flicker saat video berulang.
- 🖥️ **Desktop Icon Transparency**: Ikon desktop, seleksi marquee file, dan klik kanan desktop tetap 100% berfungsi normal dan interaktif.
- 🎮 **Smart Gaming / Fullscreen Auto-Pause**: Otomatis mem-pause video saat game fullscreen atau aplikasi layar penuh aktif untuk membebaskan 100% resource GPU & CPU.
- 🔋 **Battery & Power Saver**: Otomatis berhenti saat laptop masuk ke mode hemat daya (Low Power Mode / Battery Saver), layar mati (*display sleep*), atau layar terkunci.
- 🖥️🖥️ **Multi-Monitor Support**: Pilihan memutar video di semua layar (*All Connected Displays*) atau hanya di monitor utama (*Primary Display Only*).
- 🎛️ **Menu Bar & System Tray**: Berjalan senyap di latar belakang tanpa ikon yang mengotori Dock (macOS) maupun Taskbar (Windows).
- ⚙️ **Comprehensive Settings UI**: Pengaturan audio (Mute/Volume), mode scaling (Aspect Fill, Fit, Stretch), auto-pause rules, dan opsi Auto-start saat boot.

---

## 🏗️ Arsitektur Mesin

### 🍏 macOS Native Engine (`macos/`)
1. **Desktop Window Injection**:
   - `NSWindow` dibuat dengan `styleMask: [.borderless]` dan level `kCGDesktopWindowLevel` (`CGWindowLevelForKey(.desktopWindow)`).
   - Properti `ignoresMouseEvents = true` meneruskan semua klik dan drag langsung ke icon desktop Finder.
   - `collectionBehavior = [.canJoinAllSpaces, .stationary, .ignoresCycle]` menjaga wallpaper tetap aktif di seluruh Spaces / virtual desktop.
2. **Playback Looping**:
   - `AVQueuePlayer` dipadukan dengan `AVPlayerLooper` dan `AVPlayerLayer` (`.resizeAspectFill`), menghasilkan decoding hardware GPU VideoToolbox/Metal yang sangat hemat daya.
3. **Workspace & Screen Observers**:
   - `WorkspaceObserverService` mendengarkan `screensDidSleepNotification`, `screensDidWakeNotification`, `activeSpaceDidChangeNotification`, dan lock screen event.
   - Deteksi window fullscreen menggunakan `CGWindowListCopyWindowInfo` untuk mendeteksi aplikasi foreground yang menutupi layar secara instan.

### 🪟 Windows Native Engine (Root C# .NET 8)
1. **WorkerW Desktop Injection**:
   - Mengirim pesan `0x052C` (`WM_SPAWN_WORKER`) ke jendela `Progman` Windows Explorer.
   - Menemukan jendela `WorkerW` yang disisipkan tepat di belakang `SHELLDLL_DefView` (host icon desktop).
   - Memanggil `NativeMethods.SetParent(wallpaperHwnd, workerWHwnd)` sehingga video berada di antara background wallpaper dan ikon desktop.
2. **Playback Acceleration**:
   - Memanfaatkan DirectX / Enhanced Video Renderer (EVR) Media Foundation untuk memutar video dengan 0% CPU overhead.
3. **Fullscreen App / Game Detector**:
   - `FullscreenDetectorService` memantau `GetForegroundWindow()` dan memeriksa apakah dimensi jendela menutupi bounds resolusi monitor (mengabaikan `Shell_TrayWnd` dan `Progman`).

---

## 📁 Struktur Repositori

Struktur proyek dirancang bersih dan modular, memodelkan tata letak native dari `anime-walpaper`:

```
exawallpaper/
├── .github/
│   └── workflows/
│       ├── ci.yml                          # Validasi build CI
│       └── release.yml                     # Workflow rilis otomatis multi-platform
├── Resources/                              # Aset aplikasi
│   ├── app.ico                             # Windows app & tray icon
│   └── app.png                             # Master high-resolution app icon
├── Models/                                 # Windows Data Models
│   ├── AppConfig.cs                        # Konfigurasi JSON pengguna
│   └── WallpaperState.cs                   # Status playback
├── Services/                               # Windows Core Services
│   ├── NativeMethods.cs                    # Win32 P/Invoke API definitions
│   ├── WallpaperWindowService.cs           # WorkerW injection & monitor sizing
│   ├── FullscreenDetectorService.cs        # Deteksi jendela fullscreen & game
│   ├── PowerMonitorService.cs              # SystemEvents sleep & battery monitor
│   ├── StartupService.cs                   # Windows Registry Run key
│   └── ConfigService.cs                    # Penyimpanan konfigurasi di AppData
├── Views/                                  # Windows WPF UI
│   ├── WallpaperWindow.xaml                # Kanvas video borderless
│   ├── WallpaperWindow.xaml.cs
│   ├── SettingsWindow.xaml                 # Jendela pengaturan modern dark mode
│   └── SettingsWindow.xaml.cs
├── App.xaml                                # Application host
├── App.xaml.cs                             # Tray icon controller & app lifecycle
├── ExaWallpaper.csproj                    # Proyek .NET 8 WPF
│
├── macos/                                  # Modul Native macOS (Swift)
│   ├── Package.swift                       # Swift Package Manager manifest
│   ├── Info.plist                          # LSUIElement = true (Menu Bar app)
│   └── Sources/
│       ├── App.swift                       # NSApplication delegate
│       ├── Models/                         # AppConfig & WallpaperState
│       ├── Services/                       # DesktopWindow, VideoPlayer, WorkspaceObserver
│       ├── ViewModels/                     # WallpaperViewModel
│       └── Views/                          # MenuBarView & SwiftUI SettingsView
│
├── scripts/
│   ├── build-macos.sh                      # Script build .app bundle & packaging .zip
│   └── build-release.ps1                   # Script publish self-contained .exe Windows
├── .gitignore
├── LICENSE
└── README.md
```

---

## 🚀 Cara Menjalankan & Membangun

### macOS (Apple Silicon & Intel)
**Kebutuhan:** macOS 13.0 (Ventura) atau lebih baru, Xcode / Swift 5.9+.

1. **Jalankan langsung via terminal (Debug Mode)**:
   ```bash
   cd macos
   swift run
   ```

2. **Build Release & Packaging `.app` Bundle**:
   ```bash
   chmod +x ./scripts/build-macos.sh
   ./scripts/build-macos.sh v1.0.0
   ```
   Hasil build akan tersedia di folder `dist/macos/`:
   - `ExaWallpaper.app` (Application Bundle siap pakai)
   - `ExaWallpaper-v1.0.0-macos-arm64.zip` (Distributable zip)

### Windows (10 / 11 64-bit)
**Kebutuhan:** .NET 8.0 SDK.

1. **Jalankan langsung via terminal**:
   ```powershell
   dotnet run
   ```

2. **Publish Self-Contained Single-File Executable**:
   ```powershell
   .\scripts\build-release.ps1 -Version "v1.0.0"
   ```
   Atau melalui perintah dotnet:
   ```powershell
   dotnet publish -c Release -r win-x64 --self-contained true `
     -p:PublishSingleFile=true `
     -p:IncludeNativeLibrariesForSelfExtract=true `
     -p:EnableCompressionInSingleFile=true `
     -o ./publish
   ```
   Hasil publish berupa satu file `ExaWallpaper.exe` yang langsung dapat dijalankan tanpa perlu instalasi runtime .NET tambahan.

---

## ⚙️ Konfigurasi & Pengaturan

Konfigurasi disimpan secara otomatis di:
- **macOS**: `~/Library/Application Support/ExaWallpaper/config.json`
- **Windows**: `%APPDATA%\ExaWallpaper\config.json`

Contoh isi konfigurasi:
```json
{
  "videoPath": "/Users/username/Videos/wallpaper.mp4",
  "isMuted": true,
  "volume": 0.5,
  "scalingMode": "AspectFill",
  "autoPauseOnFullscreen": true,
  "autoPauseOnBattery": true,
  "multiMonitorMode": "AllDisplays",
  "runAtStartup": false
}
```

---

## 📄 Lisensi

Didistribusikan di bawah lisensi [MIT](LICENSE). Bebas digunakan, dimodifikasi, dan didistribusikan.
