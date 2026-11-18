using System;
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
    private WallpaperWindow? _wallpaperWindow;
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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global crash and exception logging to %APPDATA%\ExaWallpaper\error.log
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string logFolder = Path.Combine(appData, "ExaWallpaper");
        Directory.CreateDirectory(logFolder);
        string logFile = Path.Combine(logFolder, "error.log");

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            string msg = $"[UnhandledException] {args.ExceptionObject}";
            try { File.AppendAllText(logFile, $"{DateTime.Now}: {msg}\n"); } catch { }
            System.Windows.MessageBox.Show(msg, "ExaWallpaper Error", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        DispatcherUnhandledException += (s, args) =>
        {
            string msg = $"[DispatcherUnhandledException] {args.Exception}";
            try { File.AppendAllText(logFile, $"{DateTime.Now}: {msg}\n"); } catch { }
            System.Windows.MessageBox.Show(msg, "ExaWallpaper Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        Log("App OnStartup started.");

        try
        {
            IntPtr hDesk = NativeMethods.OpenDesktop("Default", 0, false, 0x01FF);
            if (hDesk != IntPtr.Zero)
            {
                bool setDesk = NativeMethods.SetThreadDesktop(hDesk);
                Log($"SetThreadDesktop(Default) returned: {setDesk}");
            }
        }
        catch (Exception ex)
        {
            Log($"SetThreadDesktop exception: {ex.Message}");
        }

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
            Log("Another instance of ExaWallpaper is already running. Exiting silently.");
            Shutdown();
            return;
        }

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

        InitializeWallpaperWindow();
        InitializeTray();
        InitializeListeners();

        // If still no video is selected, prompt user with settings
        if (string.IsNullOrEmpty(_config.VideoPath) || !File.Exists(_config.VideoPath))
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

    private void InitializeWallpaperWindow()
    {
        Log($"InitializeWallpaperWindow: VideoPath = '{_config.VideoPath}'");
        _wallpaperWindow = new WallpaperWindow();
        _wallpaperWindow.Show();
        _windowService.AttachToDesktop(_wallpaperWindow, _config);

        if (!string.IsNullOrEmpty(_config.VideoPath) && File.Exists(_config.VideoPath))
        {
            _wallpaperWindow.SetMuted(_config.IsMuted);
            _wallpaperWindow.SetVolume(_config.Volume);
            _wallpaperWindow.SetStretchMode(_config.StretchMode);
            _wallpaperWindow.LoadVideo(_config.VideoPath);
            _state.CurrentVideoPath = _config.VideoPath;
            _state.IsPlaying = true;
        }
    }

    private void InitializeTray()
    {
        _notifyIcon = new NotifyIcon
        {
            Visible = true,
            Text = "ExaWallpaper"
        };

        // Try load app icon
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
    }

    private void EvaluatePlayback()
    {
        if (_wallpaperWindow == null) return;

        Log($"EvaluatePlayback: ShouldBePlaying = {_state.ShouldBePlaying}, IsPausedByFullscreen = {_state.IsPausedByFullscreen}, IsPausedByPower = {_state.IsPausedByPower}, IsPausedByUser = {_state.IsPausedByUser}");
        if (_state.ShouldBePlaying)
        {
            _wallpaperWindow.Play();
            _state.IsPlaying = true;
            if (_playPauseMenuItem != null) _playPauseMenuItem.Text = "Pause";
        }
        else
        {
            _wallpaperWindow.Pause();
            _state.IsPlaying = false;
            if (_playPauseMenuItem != null) _playPauseMenuItem.Text = "Play";
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
        _wallpaperWindow?.SetMuted(_config.IsMuted);

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
            if (_wallpaperWindow != null)
            {
                _wallpaperWindow.SetMuted(updatedConfig.IsMuted);
                _wallpaperWindow.SetVolume(updatedConfig.Volume);
                _wallpaperWindow.SetStretchMode(updatedConfig.StretchMode);
                _windowService.UpdateWindowBounds(_wallpaperWindow, updatedConfig);

                if (!string.IsNullOrEmpty(updatedConfig.VideoPath) &&
                    updatedConfig.VideoPath != _state.CurrentVideoPath)
                {
                    _wallpaperWindow.LoadVideo(updatedConfig.VideoPath);
                    _state.CurrentVideoPath = updatedConfig.VideoPath;
                    _state.IsPlaying = true;
                }
            }

            if (_muteMenuItem != null)
            {
                _muteMenuItem.Text = updatedConfig.IsMuted ? "Unmute" : "Mute";
            }

            EvaluatePlayback();
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

        _wallpaperWindow?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
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
