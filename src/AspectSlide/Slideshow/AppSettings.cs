using AspectSlide.Model;
using AspectSlide.Windows;

namespace AspectSlide.Slideshow;

public sealed class AppSettings
{
    public string? Folder { get; set; }
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(30);
    public WallpaperFit Fit { get; set; } = WallpaperFit.Fill;
    public bool StartAtSignIn { get; set; }
    public bool Paused { get; set; }
    public bool OriginalCaptured { get; set; }
    public bool SolidColorAcknowledged { get; set; }
    public Dictionary<string, string> PreviousWallpapers { get; set; } = new(StringComparer.Ordinal);
    public DesktopWallpaperPosition PreviousPosition { get; set; } = DesktopWallpaperPosition.Fill;
    public Dictionary<string, List<string>> RecentPhotos { get; set; } = new(StringComparer.Ordinal);
}
