using AspectSlide.Windows;

namespace AspectSlide.Tests;

public sealed class WallpaperComSmokeTests
{
    [Fact]
    [Trait("Category", "Hardware")]
    public async Task ReadsRealWallpaperStateWithoutChangingIt()
    {
        if (Environment.GetEnvironmentVariable("ASPECTSLIDE_REAL_COM") != "1") return;
        var result = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var native = new ComWallpaperNative();
                var platform = new DesktopWallpaperPlatform(native);
                var displays = platform.GetAttachedDisplays();
                foreach (var display in displays) _ = platform.GetWallpaper(display.Id);
                _ = platform.GetPosition();
                _ = platform.GetNativeSlideshowActive();
                result.SetResult(displays.Count);
            }
            catch (Exception ex) { result.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(await result.Task.WaitAsync(TimeSpan.FromSeconds(10)) >= 1);
        thread.Join();
    }
}
