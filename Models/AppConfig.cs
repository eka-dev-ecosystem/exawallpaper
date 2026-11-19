using System.Collections.Generic;

namespace ExaWallpaper.Models;

public class AppConfig
{
    public string? VideoPath { get; set; } = null;
    public bool IsMuted { get; set; } = true;
    public double Volume { get; set; } = 0.5;
    public string StretchMode { get; set; } = "UniformToFill"; // UniformToFill, Uniform, Fill
    public bool AutoPauseOnFullscreen { get; set; } = true;
    public bool AutoPauseOnBattery { get; set; } = true;
    public string MultiMonitorMode { get; set; } = "Span"; // Span, PerMonitor, Duplicate, PrimaryOnly
    public Dictionary<string, string> MonitorVideos { get; set; } = new();
    public bool RunAtStartup { get; set; } = false;
}
