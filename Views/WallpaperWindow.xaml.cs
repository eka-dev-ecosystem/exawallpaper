using System;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace ExaWallpaper.Views;

public partial class WallpaperWindow : Window
{
    private bool _isMuted = true;
    private double _volume = 0.5;
    private string? _pendingVideoPath;

    public WallpaperWindow()
    {
        InitializeComponent();
        Loaded += OnWindowLoaded;
    }

    private void Log(string message)
    {
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string logFolder = Path.Combine(appData, "ExaWallpaper");
            Directory.CreateDirectory(logFolder);
            string logFile = Path.Combine(logFolder, "debug.log");
            File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}: [WallpaperWindow] {message}\n");
        }
        catch { }
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        Log($"OnWindowLoaded called, pendingVideoPath: '{_pendingVideoPath}'");
        if (!string.IsNullOrEmpty(_pendingVideoPath))
        {
            PlayVideoInternal(_pendingVideoPath);
        }
    }

    public void LoadVideo(string filePath)
    {
        Log($"LoadVideo called with: '{filePath}', IsLoaded={IsLoaded}");
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            Log($"File does not exist or empty: '{filePath}'");
            return;
        }

        _pendingVideoPath = filePath;

        if (!IsLoaded)
        {
            Log("Window not loaded yet, deferred playback to OnWindowLoaded");
            return;
        }

        PlayVideoInternal(filePath);
    }

    private void PlayVideoInternal(string filePath)
    {
        Log($"PlayVideoInternal executing for: '{filePath}'");
        Player.Source = new Uri(filePath, UriKind.Absolute);
        Player.IsMuted = _isMuted;
        Player.Volume = _volume;
        Player.Play();
        Log($"Player.Play() called, Source set to: '{Player.Source}'");
    }

    public void Play()
    {
        Log("Play() called");
        if (Player.Source != null)
        {
            Player.Play();
        }
    }

    public void Pause()
    {
        Log("Pause() called");
        if (Player.Source != null)
        {
            Player.Pause();
        }
    }

    public void SetMuted(bool isMuted)
    {
        _isMuted = isMuted;
        Player.IsMuted = isMuted;
    }

    public void SetVolume(double volume)
    {
        _volume = Math.Clamp(volume, 0.0, 1.0);
        Player.Volume = _volume;
    }

    public void SetStretchMode(string mode)
    {
        Player.Stretch = mode switch
        {
            "Uniform" => Stretch.Uniform,
            "Fill" => Stretch.Fill,
            _ => Stretch.UniformToFill
        };
    }

    private void Player_MediaOpened(object sender, RoutedEventArgs e)
    {
        Log($"Player_MediaOpened fired! HasVideo={Player.HasVideo}, Duration={Player.NaturalDuration}");
        Player.Play();
    }

    private void Player_MediaEnded(object sender, RoutedEventArgs e)
    {
        Log("Player_MediaEnded fired, looping...");
        Player.Position = TimeSpan.Zero;
        Player.Play();
    }

    private void Player_MediaFailed(object? sender, ExceptionRoutedEventArgs e)
    {
        string logMsg = $"Playback failed: {e.ErrorException?.Message}\n{e.ErrorException?.StackTrace}";
        Log(logMsg);
    }
}
