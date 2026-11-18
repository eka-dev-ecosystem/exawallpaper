using System;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace ExaWallpaper.Views;

public partial class WallpaperWindow : Window
{
    private bool _isMuted = true;
    private double _volume = 0.5;

    public WallpaperWindow()
    {
        InitializeComponent();
    }

    public void LoadVideo(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            return;
        }

        Player.Source = new Uri(filePath, UriKind.Absolute);
        Player.IsMuted = _isMuted;
        Player.Volume = _volume;
        Player.Play();
    }

    public void Play()
    {
        if (Player.Source != null)
        {
            Player.Play();
        }
    }

    public void Pause()
    {
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

    private void Player_MediaEnded(object sender, RoutedEventArgs e)
    {
        // Seamless loop back to the beginning
        Player.Position = TimeSpan.Zero;
        Player.Play();
    }

    private void Player_MediaFailed(object? sender, ExceptionRoutedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"Playback failed: {e.ErrorException?.Message}");
    }
}
