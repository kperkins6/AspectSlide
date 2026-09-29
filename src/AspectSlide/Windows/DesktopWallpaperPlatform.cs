using AspectSlide.Model;
using System.Drawing;

namespace AspectSlide.Windows;

public interface IWallpaperNative
{
    IReadOnlyList<string> EnumerateMonitorIds();
    Rectangle? GetMonitorRectangle(string id);
    string GetWallpaper(string id);
    DesktopWallpaperPosition GetPosition();
    int GetStatus();
    void SetWallpaper(string id, string path);
    void SetPosition(DesktopWallpaperPosition position);
}

public sealed class DesktopWallpaperPlatform : IWallpaperPlatform
{
    private readonly IWallpaperNative native;
    public DesktopWallpaperPlatform(IWallpaperNative native) => this.native = native;

    public IReadOnlyList<DisplayInfo> GetAttachedDisplays()
    {
        var displays = new List<DisplayInfo>();
        foreach (string id in native.EnumerateMonitorIds())
        {
            Rectangle? bounds = native.GetMonitorRectangle(id);
            if (bounds is { Width: > 0, Height: > 0 } rect)
                displays.Add(new DisplayInfo(id, rect.Width, rect.Height));
        }
        return displays;
    }

    public string GetWallpaper(string displayId) => native.GetWallpaper(displayId);
    public DesktopWallpaperPosition GetPosition() => native.GetPosition();
    public bool GetNativeSlideshowActive() => (native.GetStatus() & 0x02) != 0;
    public void SetWallpaper(string displayId, string path)
    {
        if (!GetAttachedDisplays().Any(d => d.Id == displayId))
            throw new ArgumentException("The monitor is no longer attached.", nameof(displayId));
        native.SetWallpaper(displayId, path);
    }
    public void SetPosition(DesktopWallpaperPosition position) => native.SetPosition(position);
}
