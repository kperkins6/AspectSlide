using AspectSlide.Slideshow;

namespace AspectSlide.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public void MalformedSettingsUseSafeDefaultsAndKeepOriginalFile()
    {
        using var folder = new TemporaryFolder();
        string path = Path.Combine(folder.Path, "settings.json");
        File.WriteAllText(path, "{broken");

        var settings = new SettingsStore(path).Load();

        Assert.Null(settings.Folder);
        Assert.Equal(TimeSpan.FromMinutes(30), settings.Interval);
        Assert.Equal("{broken", File.ReadAllText(path));
    }

    [Fact]
    public void OutOfRangeIntervalFallsBackToSafeDefault()
    {
        using var folder = new TemporaryFolder();
        var store = new SettingsStore(Path.Combine(folder.Path, "settings.json"));
        store.Save(new AppSettings { Interval = TimeSpan.Zero });

        Assert.Equal(TimeSpan.FromMinutes(30), store.Load().Interval);
    }
}
