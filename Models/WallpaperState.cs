namespace ExaWallpaper.Models;

public class WallpaperState
{
    public bool IsPlaying { get; set; } = false;
    public string? CurrentVideoPath { get; set; }
    public bool IsPausedByUser { get; set; } = false;
    public bool IsPausedByFullscreen { get; set; } = false;
    public bool IsPausedByPower { get; set; } = false;

    public bool ShouldBePlaying => !string.IsNullOrEmpty(CurrentVideoPath)
                                  && !IsPausedByUser
                                  && !IsPausedByFullscreen
                                  && !IsPausedByPower;
}
