using AspectSlide.Windows;
using System.Drawing;

namespace AspectSlide.Tests;

public sealed class WallpaperPlatformContractTests
{
    [Fact]
    public void DetachedMonitorIsExcludedAndCannotBeWritten()
    {
        var native = new FakeNative();
        var platform = new DesktopWallpaperPlatform(native);

        Assert.Equal(2, platform.GetAttachedDisplays().Count);
        Assert.Throws<ArgumentException>(() => platform.SetWallpaper("detached", "x.jpg"));
        platform.SetWallpaper("wide", "one.jpg");
        platform.SetWallpaper("tall", "two.jpg");
        Assert.Equal("one.jpg", native.Wallpapers["wide"]);
        Assert.Equal("two.jpg", native.Wallpapers["tall"]);
    }

    [Theory]
    [InlineData(DesktopWallpaperPosition.Center)]
    [InlineData(DesktopWallpaperPosition.Tile)]
    [InlineData(DesktopWallpaperPosition.Stretch)]
    [InlineData(DesktopWallpaperPosition.Fit)]
    [InlineData(DesktopWallpaperPosition.Fill)]
    [InlineData(DesktopWallpaperPosition.Span)]
    public void NativePositionRoundTripsWithoutLoss(DesktopWallpaperPosition position)
    {
        var platform = new DesktopWallpaperPlatform(new FakeNative());
        platform.SetPosition(position);
        Assert.Equal(position, platform.GetPosition());
    }

    [Fact]
    public void ConfiguredSlideshowBlocksEvenDuringRemoteSession()
    {
        var native = new FakeNative { Status = 0x02 | 0x04 };
        Assert.True(new DesktopWallpaperPlatform(native).GetNativeSlideshowActive());
        native.Status = 0x01;
        Assert.False(new DesktopWallpaperPlatform(native).GetNativeSlideshowActive());
    }

    [Fact]
    public void EmptyWallpaperPathIsPreservedAsSolidColor()
    {
        var platform = new DesktopWallpaperPlatform(new FakeNative());
        Assert.Equal("", platform.GetWallpaper("wide"));
    }

    private sealed class FakeNative : IWallpaperNative
    {
        public Dictionary<string, string> Wallpapers { get; } = new(StringComparer.Ordinal)
            { ["wide"] = "", ["tall"] = "" };
        public DesktopWallpaperPosition Position { get; set; } = DesktopWallpaperPosition.Fill;
        public int Status { get; set; }
        public IReadOnlyList<string> EnumerateMonitorIds() => ["wide", "tall", "detached"];
        public Rectangle? GetMonitorRectangle(string id) => id switch
        {
            "wide" => new Rectangle(0, 0, 1920, 1080),
            "tall" => new Rectangle(1920, 0, 1080, 1920),
            _ => null
        };
        public string GetWallpaper(string id) => Wallpapers[id];
        public DesktopWallpaperPosition GetPosition() => Position;
        public int GetStatus() => Status;
        public void SetWallpaper(string id, string path) => Wallpapers[id] = path;
        public void SetPosition(DesktopWallpaperPosition position) => Position = position;
    }
}
