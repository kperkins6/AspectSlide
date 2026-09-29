using AspectSlide.UI;

namespace AspectSlide.Tests;

public sealed class StartupShortcutTests
{
    [Fact]
    public void EnablingAndDisablingAffectsOnlyAppsOwnShortcut()
    {
        using var folder = new TemporaryFolder();
        string executable = Path.Combine(folder.Path, "AspectSlide.exe");
        File.WriteAllText(executable, "test");
        string link = Path.Combine(folder.Path, "AspectSlide.lnk");
        var shortcut = new StartupShortcut(folder.Path, executable);

        Assert.False(File.Exists(link));
        shortcut.SetEnabled(true);
        Assert.True(File.Exists(link));
        shortcut.SetEnabled(false);
        Assert.False(File.Exists(link));

        File.WriteAllText(link, "foreign shortcut");
        shortcut.SetEnabled(false);
        Assert.True(File.Exists(link));
    }
}
