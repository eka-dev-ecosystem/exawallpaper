using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using ExaWallpaper.Models;
using ExaWallpaper.Services;
using ExaWallpaper.Views;
using Application = System.Windows.Application;

namespace ExaWallpaper;

public partial class App : Application
{
    private NotifyIcon? _notifyIcon;
    private readonly List<WallpaperWindow> _wallpaperWindows = new();
    private SettingsWindow? _settingsWindow;

    private ConfigService _configService = null!;
    private WallpaperWindowService _windowService = null!;
    private FullscreenDetectorService _fullscreenDetector = null!;
    private PowerMonitorService _powerMonitor = null!;

    private AppConfig _config = null!;
    private readonly WallpaperState _state = new();

    private ToolStripMenuItem? _playPauseMenuItem;
    private ToolStripMenuItem? _muteMenuItem;

    private static System.Threading.Mutex? _singleInstanceMutex;
    private static readonly uint WM_SHOW_SETTINGS = NativeMethods.RegisterWindowMessage("ExaWallpaper_ShowSettings_Message");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Global crash and exception logging to %APPDATA%\ExaWallpaper\error.log
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string logFolder = Path.Combine(appData, "ExaWallpaper");
        Directory.CreateDirectory(logFolder);
        string logFile = Path.Combine(logFolder, "error.log");

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            string msg = $"[UnhandledException] {args.ExceptionObject}";
            try { File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}: {msg}\n"); } catch { }
            System.Windows.MessageBox.Show(msg, "ExaWallpaper Error", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        DispatcherUnhandledException += (s, args) =>
        {
            string msg = $"[DispatcherUnhandledException] {args.Exception}";
            try { File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}: {msg}\n"); } catch { }
            System.Windows.MessageBox.Show(msg, "ExaWallpaper Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        Log("App OnStartup started.");

        // Enforce single-instance on Windows
        bool isNewInstance = false;
        try
        {
            _singleInstanceMutex = new System.Threading.Mutex(true, "ExaWallpaper_SingleInstance_Mutex", out isNewInstance);
        }
        catch (AbandonedMutexException)
        {
            Log("Mutex was abandoned by previous instance; acquiring ownership.");
            isNewInstance = true;
        }
        catch (Exception ex)
        {
            Log($"Mutex acquisition error: {ex.Message}; proceeding as new instance.");
            isNewInstance = true;
        }

        if (!isNewInstance)
        {
            Log("Another instance of ExaWallpaper is already running. Broadcasting WM_SHOW_SETTINGS and exiting.");
            NativeMethods.PostMessage(new IntPtr(NativeMethods.HWND_BROADCAST), WM_SHOW_SETTINGS, IntPtr.Zero, IntPtr.Zero);
            Shutdown();
            return;
        }

        ComponentDispatcher.ThreadFilterMessage += (ref MSG msg, ref bool handled) =>
        {
            if (msg.message == WM_SHOW_SETTINGS)
            {
                Log("Received WM_SHOW_SETTINGS broadcast; opening SettingsWindow.");
                Dispatcher.Invoke(() => ShowSettings());
                handled = true;
            }
        };

        _configService = new ConfigService();
        _config = _configService.Load();

        // If no video is selected yet, check for bundled sample wallpaper
        if (string.IsNullOrEmpty(_config.VideoPath) || !File.Exists(_config.VideoPath))
        {
            string samplePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sample-wallpapers", "gojo_satoru_live.mp4");
            if (File.Exists(samplePath))
            {
                _config.VideoPath = samplePath;
                _configService.Save(_config);
            }
        }

        _windowService = new WallpaperWindowService();
        _fullscreenDetector = new FullscreenDetectorService(1000);
        _powerMonitor = new PowerMonitorService();

        ReconfigureWallpaperWindows();
        InitializeTray();
        InitializeListeners();

        bool isStartupLaunch = false;
        foreach (var arg in e.Args)
        {
            if (arg.Equals("--startup", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--tray", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase))
            {
                isStartupLaunch = true;
                break;
            }
        }

        if (!isStartupLaunch)
        {
            ShowSettings();
        }
    }

    private static void Log(string message)
    {
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string logFolder = Path.Combine(appData, "ExaWallpaper");
            Directory.CreateDirectory(logFolder);
            string logFile = Path.Combine(logFolder, "debug.log");
            File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}: [App] {message}\n");
        }
        catch { }
    }

    private void ReconfigureWallpaperWindows()
    {
        Log($"ReconfigureWallpaperWindows: Mode = '{_config.MultiMonitorMode}', DefaultVideo = '{_config.VideoPath}'");
        _state.CurrentVideoPath = _config.VideoPath;

        foreach (var win in _wallpaperWindows)
        {
            _windowService.DetachWindow(win);
            win.Close();
        }
        _wallpaperWindows.Clear();

        switch (_config.MultiMonitorMode)
        {
            case "Span":
                // "ke 3 monitor jadi satu" - VirtualScreen spanning across all connected monitors
                var vs = SystemInformation.VirtualScreen;
                CreateWindowForBounds(vs.X, vs.Y, vs.Width, vs.Height, _config.VideoPath);
                break;

            case "PerMonitor":
                // "setiap monitor bisa beda beda" - Separate window for each screen with monitor-specific video
                var allScreens = Screen.AllScreens;
                for (int i = 0; i < allScreens.Length; i++)
                {
                    var s = allScreens[i];
                    string? video = _config.MonitorVideos.TryGetValue(i.ToString(), out var p) && File.Exists(p)
                        ? p
                        : _config.VideoPath;
                    CreateWindowForBounds(s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height, video);
                }
                break;

            case "PrimaryOnly":
                var primary = Screen.PrimaryScreen?.Bounds ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
                CreateWindowForBounds(primary.X, primary.Y, primary.Width, primary.Height, _config.VideoPath);
                break;

            default: // "Duplicate"
                foreach (var s in Screen.AllScreens)
                {
                    CreateWindowForBounds(s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height, _config.VideoPath);
                }
                break;
        }

        EvaluatePlayback();
    }

    private void CreateWindowForBounds(int x, int y, int width, int height, string? videoPath)
    {
        var win = new WallpaperWindow();
        win.SetMuted(_config.IsMuted);
        win.SetVolume(_config.Volume);
        win.SetStretchMode(_config.StretchMode);

        win.Show();
        _windowService.AttachToDesktop(win, x, y, width, height);

        if (!string.IsNullOrEmpty(videoPath) && File.Exists(videoPath))
        {
            win.LoadVideo(videoPath);
        }

        _wallpaperWindows.Add(win);
    }

    private void InitializeTray()
    {
        _notifyIcon = new NotifyIcon
        {
            Visible = true,
            Text = "ExaWallpaper"
        };

        string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "app.ico");
        if (File.Exists(iconPath))
        {
            _notifyIcon.Icon = new Icon(iconPath);
        }
        else
        {
            _notifyIcon.Icon = SystemIcons.Application;
        }

        var contextMenu = new ContextMenuStrip();

        _playPauseMenuItem = new ToolStripMenuItem("Pause", null, OnTogglePlayPause);
        _muteMenuItem = new ToolStripMenuItem(_config.IsMuted ? "Unmute" : "Mute", null, OnToggleMute);

        var settingsItem = new ToolStripMenuItem("Settings...", null, (s, e) => ShowSettings());
        var exitItem = new ToolStripMenuItem("Exit", null, (s, e) => ShutdownApp());

        contextMenu.Items.Add(_playPauseMenuItem);
        contextMenu.Items.Add(_muteMenuItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(settingsItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.DoubleClick += (s, e) => ShowSettings();
        _notifyIcon.ShowBalloonTip(2000, "ExaWallpaper", "ExaWallpaper aktif di system tray.", ToolTipIcon.Info);
    }

    private void InitializeListeners()
    {
        _fullscreenDetector.FullscreenStateChanged += isFullscreen =>
        {
            if (!_config.AutoPauseOnFullscreen) return;

            _state.IsPausedByFullscreen = isFullscreen;
            EvaluatePlayback();
        };
        _fullscreenDetector.Start();

        _powerMonitor.PowerPauseStateChanged += shouldPause =>
        {
            if (!_config.AutoPauseOnBattery) return;

            _state.IsPausedByPower = shouldPause;
            EvaluatePlayback();
        };

        // Listen for display changes (monitors plugged / unplugged)
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (s, e) =>
        {
            Dispatcher.Invoke(ReconfigureWallpaperWindows);
        };
    }

    private void EvaluatePlayback()
    {
        Log($"EvaluatePlayback: ShouldBePlaying = {_state.ShouldBePlaying}, WindowsCount = {_wallpaperWindows.Count}");
        foreach (var win in _wallpaperWindows)
        {
            if (_state.ShouldBePlaying)
            {
                win.Play();
            }
            else
            {
                win.Pause();
            }
        }

        _state.IsPlaying = _state.ShouldBePlaying && _wallpaperWindows.Count > 0;
        if (_playPauseMenuItem != null)
        {
            _playPauseMenuItem.Text = _state.IsPlaying ? "Pause" : "Play";
        }
    }

    private void OnTogglePlayPause(object? sender, EventArgs e)
    {
        _state.IsPausedByUser = !_state.IsPausedByUser;
        EvaluatePlayback();
    }

    private void OnToggleMute(object? sender, EventArgs e)
    {
        _config.IsMuted = !_config.IsMuted;
        _configService.Save(_config);
        foreach (var win in _wallpaperWindows)
        {
            win.SetMuted(_config.IsMuted);
        }

        if (_muteMenuItem != null)
        {
            _muteMenuItem.Text = _config.IsMuted ? "Unmute" : "Mute";
        }
    }

    private void ShowSettings()
    {
        if (_settingsWindow != null && _settingsWindow.IsVisible)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_config, _configService);
        _settingsWindow.ConfigApplied += updatedConfig =>
        {
            _config = updatedConfig;
            ReconfigureWallpaperWindows();

            if (_muteMenuItem != null)
            {
                _muteMenuItem.Text = updatedConfig.IsMuted ? "Unmute" : "Mute";
            }
        };

        _settingsWindow.Topmost = true;
        _settingsWindow.Show();
        _settingsWindow.Activate();
        _settingsWindow.Focus();
        _settingsWindow.Topmost = false;
    }

    private void ShutdownApp()
    {
        _fullscreenDetector.Stop();
        _powerMonitor.Dispose();
        _windowService?.RestoreDesktop();

        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }

        if (_singleInstanceMutex != null)
        {
            try { _singleInstanceMutex.ReleaseMutex(); } catch { }
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }

        foreach (var win in _wallpaperWindows)
        {
            win.Close();
        }
        _wallpaperWindows.Clear();

        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log($"App OnExit called with exitCode: {e.ApplicationExitCode}");
        _windowService?.RestoreDesktop();
        if (_singleInstanceMutex != null)
        {
            try { _singleInstanceMutex.ReleaseMutex(); } catch { }
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }
        base.OnExit(e);
    }
}
