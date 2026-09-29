using AspectSlide.Catalog;
using AspectSlide.Model;
using AspectSlide.Slideshow;
using AspectSlide.UI;
using AspectSlide.Windows;

namespace AspectSlide.Tests;

public sealed class SettingsFormActionTests
{
    [Fact]
    public async Task EverySettingsActionIsWiredWithoutTouchingTheRealDesktop()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { ExerciseActions(); completion.SetResult(); }
            catch (Exception ex) { completion.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }

    private static void ExerciseActions()
    {
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        using var folder = new TemporaryFolder();
        using var nextFolder = new TemporaryFolder();
        string wide = Path.Combine(folder.Path, "wide.jpg");
        string tall = Path.Combine(folder.Path, "tall.jpg");
        string beforeWide = Path.Combine(folder.Path, "before-wide.jpg");
        string beforeTall = Path.Combine(folder.Path, "before-tall.jpg");
        foreach (string path in new[] { wide, tall, beforeWide, beforeTall }) File.WriteAllText(path, "test");
        var settings = new AppSettings { Folder = folder.Path };
        var store = new SettingsStore(Path.Combine(folder.Path, "settings.json"));
        var platform = new FakePlatform(beforeWide, beforeTall);
        using var catalog = new FakeCatalog([new(wide, 1920, 1080), new(tall, 1080, 1920)]);
        var controller = new SlideshowController(settings, store, catalog, new FakePreparer(), platform,
            new SystemClock(), new FakeTimer(), new SynchronizationContext());
        var opened = new List<string>();
        int exits = 0;
        using var form = new SettingsForm(settings, store, controller,
            new StartupShortcut(folder.Path, Path.Combine(folder.Path, "AspectSlide.exe")), platform,
            () => exits++, uri => { opened.Add(uri); return Task.CompletedTask; }, _ => nextFolder.Path)
        { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-10000, -10000) };
        try
        {
            form.Show();
            Click(form, "Change now");
            PumpUntil(() => platform.Writes >= 2);
            Click(form, "Check again");
            PumpUntil(() => platform.Writes >= 4);

            Click(form, "Pause");
            Assert.True(settings.Paused);
            Click(form, "Resume");
            Assert.False(settings.Paused);

            Click(form, "Restore previous");
            PumpUntil(() => settings.Paused && platform.Wallpapers["wide"] == beforeWide && platform.Wallpapers["tall"] == beforeTall);
            Assert.Equal(beforeTall, platform.Wallpapers["tall"]);

            Click(form, "Windows colors");
            Click(form, "Windows background");
            Assert.Equal(["ms-settings:colors", "ms-settings:personalization-background"], opened);

            var fit = Find<ComboBox>(form).Single();
            fit.SelectedIndex = 1;
            Assert.Equal(WallpaperFit.Fit, settings.Fit);
            Find<NumericUpDown>(form).Single().Value = 5;
            Assert.Equal(TimeSpan.FromMinutes(5), settings.Interval);

            var signIn = Find<CheckBox>(form).Single();
            signIn.Checked = true;
            Assert.True(settings.StartAtSignIn);
            Assert.True(File.Exists(Path.Combine(folder.Path, "AspectSlide.lnk")));
            signIn.Checked = false;
            Assert.False(settings.StartAtSignIn);

            Click(form, "Browse");
            PumpUntil(() => settings.Folder == nextFolder.Path);
            Click(form, "Exit");
            Assert.Equal(1, exits);
        }
        finally { controller.Stop(); form.Hide(); }
    }

    private static void Click(Form form, string text)
    {
        var button = Find<Button>(form).Single(b => b.Text.StartsWith(text, StringComparison.Ordinal));
        button.PerformClick();
        Application.DoEvents();
    }

    private static IEnumerable<T> Find<T>(Control parent) where T : Control
    {
        foreach (Control child in parent.Controls)
        {
            if (child is T found) yield return found;
            foreach (T descendant in Find<T>(child)) yield return descendant;
        }
    }

    private static void PumpUntil(Func<bool> ready)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!ready() && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(10); }
        Assert.True(ready());
    }

    private sealed class FakeCatalog(IReadOnlyList<PhotoInfo> photos) : IPhotoCatalog
    {
        public IReadOnlyList<PhotoInfo> Current => photos;
        public IReadOnlyList<PhotoInfo> Scan(string root) => photos;
        public void StartWatching(string root, Action changed) { }
        public void RetryWatcher() { }
        public void Dispose() { }
    }

    private sealed class FakePreparer : IWallpaperImagePreparer
    {
        public string Prepare(PhotoInfo photo) => photo.Path;
        public void Prune(IReadOnlySet<string> activeWallpaperPaths) { }
    }

    private sealed class FakeTimer : IAppTimer
    {
        public void Arm(TimeSpan delay, Action tick) { }
        public void Dispose() { }
    }

    private sealed class FakePlatform(string wide, string tall) : IWallpaperPlatform
    {
        public Dictionary<string, string> Wallpapers { get; } = new() { ["wide"] = wide, ["tall"] = tall };
        public int Writes { get; private set; }
        public IReadOnlyList<DisplayInfo> GetAttachedDisplays() => [new("wide", 1920, 1080), new("tall", 1080, 1920)];
        public string GetWallpaper(string id) => Wallpapers[id];
        public DesktopWallpaperPosition GetPosition() => DesktopWallpaperPosition.Fill;
        public bool GetNativeSlideshowActive() => false;
        public void SetWallpaper(string id, string path) { Wallpapers[id] = path; Writes++; }
        public void SetPosition(DesktopWallpaperPosition position) { }
    }
}
