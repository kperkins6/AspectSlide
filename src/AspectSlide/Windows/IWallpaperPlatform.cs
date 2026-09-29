using AspectSlide.Model;

namespace AspectSlide.Windows;

public enum DesktopWallpaperPosition { Center = 0, Tile = 1, Stretch = 2, Fit = 3, Fill = 4, Span = 5 }

public interface IWallpaperPlatform
{
    IReadOnlyList<DisplayInfo> GetAttachedDisplays();
    string GetWallpaper(string displayId);
    DesktopWallpaperPosition GetPosition();
    bool GetNativeSlideshowActive();
    void SetWallpaper(string displayId, string path);
    void SetPosition(DesktopWallpaperPosition position);
}
