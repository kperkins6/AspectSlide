using AspectSlide.Slideshow;
using AspectSlide.UI;

namespace AspectSlide.Tests;

public sealed class LaunchPresentationTests
{
    [Fact]
    public void ManualLaunchOpensSettingsWithOrWithoutAFolder()
    {
        Assert.True(LaunchPresentation.NeedsSettingsWindow(new AppSettings(), background: false));
        Assert.True(LaunchPresentation.NeedsSettingsWindow(new AppSettings { Folder = @"C:\Photos" }, background: false));
    }

    [Fact]
    public void BackgroundLaunchStaysInTrayOnlyWhenConfigured()
    {
        Assert.False(LaunchPresentation.NeedsSettingsWindow(new AppSettings { Folder = @"C:\Photos" }, background: true));
        Assert.True(LaunchPresentation.NeedsSettingsWindow(new AppSettings(), background: true));
    }
}
