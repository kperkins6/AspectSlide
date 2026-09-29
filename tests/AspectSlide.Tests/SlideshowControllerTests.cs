using AspectSlide.Catalog;
using AspectSlide.Model;
using AspectSlide.Slideshow;
using AspectSlide.Windows;

namespace AspectSlide.Tests;

public sealed class SlideshowControllerTests
{
    [Fact]
    public async Task ChangeNowAssignsDistinctImagesAndSavesRestorablePosition()
    {
        using var fixture = new ControllerFixture();
        fixture.Platform.Position = DesktopWallpaperPosition.Span;
        await fixture.Controller.ChangeNowAsync();

        Assert.Equal(2, fixture.Platform.Assignments.Count);
        Assert.Equal(2, fixture.Platform.Assignments.Select(a => a.Path).Distinct().Count());
        Assert.Equal(DesktopWallpaperPosition.Span, fixture.Settings.PreviousPosition);
        Assert.Equal(fixture.BeforeWide, fixture.Settings.PreviousWallpapers["wide"]);
        await fixture.Controller.RestorePreviousAsync();
        Assert.Equal(DesktopWallpaperPosition.Span, fixture.Platform.Position);
        Assert.Equal(fixture.BeforeWide, fixture.Platform.Wallpapers["wide"]);
    }

    [Fact]
    public async Task RestoringPreviousWallpapersPausesFutureAutomaticChanges()
    {
        using var fixture = new ControllerFixture();
        await fixture.Controller.ChangeNowAsync();

        await fixture.Controller.RestorePreviousAsync();

        Assert.True(fixture.Settings.Paused);
        int assignments = fixture.Platform.Assignments.Count;
        await fixture.Controller.OnTimerAsync();
        Assert.Equal(assignments, fixture.Platform.Assignments.Count);
    }

    [Fact]
    public async Task EditingIntervalRearmsNextChangeFromNow()
    {
        using var fixture = new ControllerFixture();
        await fixture.Controller.ChangeNowAsync();

        fixture.Controller.UpdateInterval(TimeSpan.FromMinutes(5));

        Assert.Equal(TimeSpan.FromMinutes(5), fixture.Settings.Interval);
        Assert.Equal(TimeSpan.FromMinutes(5), fixture.Timer.LastDelay);
    }

    [Fact]
    public async Task PauseBlocksAutomaticTickButAllowsManualChange()
    {
        using var fixture = new ControllerFixture();
        fixture.Controller.SetPaused(true);
        await fixture.Controller.OnTimerAsync();
        Assert.Empty(fixture.Platform.Assignments);

        await fixture.Controller.ChangeNowAsync();
        Assert.Equal(2, fixture.Platform.Assignments.Count);
    }

    [Fact]
    public async Task PersistedPausePreventsStartupChange()
    {
        using var fixture = new ControllerFixture();
        fixture.Settings.Paused = true;

        await fixture.Controller.StartAsync();

        Assert.Empty(fixture.Platform.Assignments);
    }

    [Fact]
    public async Task SolidColorDesktopWaitsForAcknowledgementBeforeFirstChange()
    {
        using var fixture = new ControllerFixture();
        fixture.Platform.Wallpapers["wide"] = "";

        await fixture.Controller.StartAsync();
        Assert.Empty(fixture.Platform.Assignments);
        Assert.Contains("solid", fixture.Controller.Status, StringComparison.OrdinalIgnoreCase);

        fixture.Controller.AcknowledgeSolidColorRestoreLimitation();
        await fixture.Controller.ChangeNowAsync();
        Assert.Equal(2, fixture.Platform.Assignments.Count);
        Assert.Equal("", fixture.Settings.PreviousWallpapers["wide"]);
    }

    [Fact]
    public async Task PreparesEntireBatchBeforeWritingAnyMonitor()
    {
        using var fixture = new ControllerFixture();
        fixture.Preparer.OnPrepare = _ =>
        {
            Assert.Empty(fixture.Platform.Assignments);
            return null;
        };

        await fixture.Controller.ChangeNowAsync();

        Assert.Equal(2, fixture.Preparer.Calls);
        Assert.Equal(2, fixture.Platform.Assignments.Count);
    }

    [Fact]
    public async Task NativeSlideshowPreventsWallpaperChanges()
    {
        using var fixture = new ControllerFixture();
        fixture.Platform.NativeSlideshow = true;
        await fixture.Controller.ChangeNowAsync();
        Assert.Empty(fixture.Platform.Assignments);
        Assert.Contains("slideshow", fixture.Controller.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PausingWhilePreparationRunsPreventsAutomaticWallpaperChanges()
    {
        using var fixture = new ControllerFixture();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        fixture.Preparer.OnPrepare = _ => { entered.Set(); release.Wait(TimeSpan.FromSeconds(5)); return null; };

        Task change = fixture.Controller.OnTimerAsync();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
        fixture.Controller.SetPaused(true);
        release.Set();
        await change;

        Assert.Empty(fixture.Platform.Assignments);
    }

    [Fact]
    public async Task TimerAndResumeBurstRunsOneBatch()
    {
        using var fixture = new ControllerFixture();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        fixture.Preparer.OnPrepare = _ => { entered.Set(); release.Wait(TimeSpan.FromSeconds(5)); return null; };

        Task first = fixture.Controller.OnTimerAsync();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
        await fixture.Controller.OnTimerAsync();
        await fixture.Controller.OnResumeAsync();
        await fixture.Controller.OnDisplaysChangedAsync();
        release.Set();
        await first;

        Assert.Equal(2, fixture.Platform.Assignments.Count);
    }

    [Fact]
    public async Task FailedSelectedImageIsExcludedAndReplaced()
    {
        using var fixture = new ControllerFixture();
        bool first = true;
        fixture.Preparer.OnPrepare = _ =>
        {
            if (first) { first = false; throw new IOException("decode failed"); }
            return null;
        };

        await fixture.Controller.ChangeNowAsync();

        Assert.Equal(2, fixture.Platform.Assignments.Count);
        Assert.InRange(fixture.Preparer.Calls, 3, 8);
    }

    [Fact]
    public async Task DecoderErrorDuringPreparationDoesNotAbortChangeNow()
    {
        using var fixture = new ControllerFixture();
        bool first = true;
        fixture.Preparer.OnPrepare = _ =>
        {
            if (first) { first = false; throw new System.Runtime.InteropServices.ExternalException("decode failed"); }
            return null;
        };

        await fixture.Controller.ChangeNowAsync();

        Assert.Equal(2, fixture.Platform.Assignments.Count);
    }

    [Fact]
    public async Task CachePruningKeepsTheWallpaperOfAMonitorWhoseWriteFailed()
    {
        using var fixture = new ControllerFixture();
        fixture.Platform.FailOn = "tall";

        await fixture.Controller.ChangeNowAsync();

        Assert.Contains(fixture.Platform.Wallpapers["tall"], fixture.Preparer.LastPrunePaths);
    }

    [Fact]
    public async Task AllWallpaperWritesFailingSchedulesRecoverySoon()
    {
        using var fixture = new ControllerFixture();
        fixture.Platform.FailAll = true;

        await fixture.Controller.ChangeNowAsync();

        Assert.Empty(fixture.Platform.Assignments);
        Assert.InRange(fixture.Timer.LastDelay, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task EmptyCatalogArmsARecoveryTimer()
    {
        using var fixture = new ControllerFixture();
        foreach (string file in Directory.EnumerateFiles(fixture.Settings.Folder!)) File.Delete(file);

        await fixture.Controller.ChangeNowAsync();

        Assert.True(fixture.Timer.ArmCount > 0);
        Assert.InRange(fixture.Timer.LastDelay, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task ReturningFolderIsRescannedBeforeUsingTheOldSnapshot()
    {
        using var fixture = new ControllerFixture();
        await fixture.Controller.ChangeNowAsync();
        int scans = fixture.Catalog.ScanCalls;
        string path = fixture.Settings.Folder!;
        string away = path + "-away";
        Directory.Move(path, away);
        try
        {
            await fixture.Controller.ChangeNowAsync();
            Directory.Move(away, path);
            File.WriteAllText(Path.Combine(path, "new.jpg"), "new");

            await fixture.Controller.ChangeNowAsync();

            Assert.True(fixture.Catalog.ScanCalls > scans);
            Assert.Equal(5, fixture.Controller.LibraryCount);
        }
        finally { if (Directory.Exists(away) && !Directory.Exists(path)) Directory.Move(away, path); }
    }

    [Fact]
    public async Task FastBatchAbsorbsPendingDisplayEvent()
    {
        using var fixture = new ControllerFixture();
        Task displayEvent = fixture.Controller.OnDisplaysChangedAsync();
        await fixture.Controller.OnTimerAsync();
        await displayEvent;

        Assert.Equal(2, fixture.Platform.Assignments.Count);
    }

    [Fact]
    public async Task ActualMonitorRotationTriggersANewBatch()
    {
        using var fixture = new ControllerFixture();
        await fixture.Controller.ChangeNowAsync();
        fixture.Platform.Displays = [new("wide", 1080, 1920), new("tall", 1080, 1920)];

        await fixture.Controller.OnDisplaysChangedAsync();

        Assert.Equal(4, fixture.Platform.Assignments.Count);
    }

    [Fact]
    public async Task PauseAfterTopologyRefreshPreventsQueuedWallpaperWrite()
    {
        using var fixture = new ControllerFixture();
        fixture.Platform.OnGetDisplays = count =>
        {
            if (count == 3) fixture.Controller.SetPaused(true);
        };

        await fixture.Controller.OnTimerAsync();

        Assert.Empty(fixture.Platform.Assignments);
    }

    [Fact]
    public async Task FailedImageIsNotRetriedOnLaterChangesInSameSession()
    {
        using var fixture = new ControllerFixture();
        string? failedPath = null;
        fixture.Preparer.OnPrepare = photo =>
        {
            failedPath ??= photo.Path;
            if (photo.Path == failedPath) throw new IOException("decode failed");
            return null;
        };
        await fixture.Controller.ChangeNowAsync();
        int firstBatchAttempts = fixture.Preparer.AttemptPaths.Count;

        await fixture.Controller.ChangeNowAsync();

        Assert.DoesNotContain(failedPath, fixture.Preparer.AttemptPaths.Skip(firstBatchAttempts));
    }

    [Fact]
    public async Task SwitchingFoldersDuringPreparationCancelsTheOldBatch()
    {
        using var fixture = new ControllerFixture();
        using var nextFolder = new TemporaryFolder();
        File.WriteAllText(Path.Combine(nextFolder.Path, "wide-new.jpg"), "new wide");
        File.WriteAllText(Path.Combine(nextFolder.Path, "tall-new.jpg"), "new tall");
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        fixture.Preparer.OnPrepare = _ =>
        {
            if (!entered.IsSet) { entered.Set(); release.Wait(TimeSpan.FromSeconds(5)); }
            return null;
        };

        Task oldBatch = fixture.Controller.OnTimerAsync();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
        Task switchFolder = fixture.Controller.SelectFolderAsync(nextFolder.Path);
        release.Set();
        await Task.WhenAll(oldBatch, switchFolder);

        Assert.Equal(2, fixture.Platform.Assignments.Count);
        Assert.All(fixture.Platform.Assignments, a => Assert.StartsWith(nextFolder.Path, a.Path));
    }

    [Fact]
    public async Task LaterChangesUseCatalogSnapshotInsteadOfReprobingWholeFolder()
    {
        using var fixture = new ControllerFixture();
        await fixture.Controller.ChangeNowAsync();
        int firstScan = fixture.Catalog.ScanCalls;

        await fixture.Controller.ChangeNowAsync();

        Assert.Equal(firstScan, fixture.Catalog.ScanCalls);
    }

    [Fact]
    public async Task SettingsCanShowLibraryCountAndOriginalSourceNames()
    {
        using var fixture = new ControllerFixture();
        fixture.Preparer.OnPrepare = photo => photo.Path + ".prepared";

        await fixture.Controller.ChangeNowAsync();

        Assert.Equal(4, fixture.Controller.LibraryCount);
        string prepared = fixture.Platform.Wallpapers["wide"];
        string? source = fixture.Controller.GetCurrentSourcePath("wide", prepared);
        Assert.Equal(fixture.Settings.RecentPhotos["wide"].Last(), source);
        Assert.Null(fixture.Controller.GetCurrentSourcePath("wide", "a-different-wallpaper.jpg"));
    }

    private sealed class ControllerFixture : IDisposable
    {
        private readonly TemporaryFolder folder = new();
        public AppSettings Settings { get; }
        public FakePlatform Platform { get; } = new();
        public FakePreparer Preparer { get; } = new();
        public FakeProbe Probe { get; } = new();
        public CountingCatalog Catalog { get; }
        public FakeTimer Timer { get; } = new();
        public SlideshowController Controller { get; }
        public string BeforeWide => Path.Combine(folder.Path, "before-wide.jpg");

        public ControllerFixture()
        {
            File.WriteAllText(Path.Combine(folder.Path, "wide.jpg"), "wide");
            File.WriteAllText(Path.Combine(folder.Path, "tall.jpg"), "tall");
            File.WriteAllText(BeforeWide, "old wide");
            string beforeTall = Path.Combine(folder.Path, "before-tall.jpg");
            File.WriteAllText(beforeTall, "old tall");
            Platform.Wallpapers["wide"] = BeforeWide;
            Platform.Wallpapers["tall"] = beforeTall;
            Settings = new AppSettings { Folder = folder.Path };
            Catalog = new CountingCatalog(new PhotoCatalog(Probe));
            Controller = new SlideshowController(Settings,
                new SettingsStore(Path.Combine(folder.Path, "settings.json")),
                Catalog, Preparer, Platform,
                new FakeClock(), Timer, new SynchronizationContext());
        }
        public void Dispose() { Controller.Stop(); folder.Dispose(); }
    }

    public sealed class FakeProbe : IPhotoProbe
    {
        public int Calls { get; private set; }
        public bool TryRead(string path, out PhotoInfo photo)
        {
            Calls++;
            bool wide = Path.GetFileName(path).StartsWith("wide", StringComparison.Ordinal);
            photo = new PhotoInfo(path, wide ? 1920 : 1080, wide ? 1080 : 1920);
            return true;
        }
    }

    public sealed class CountingCatalog : IPhotoCatalog
    {
        private readonly PhotoCatalog inner;
        public int ScanCalls { get; private set; }
        public CountingCatalog(PhotoCatalog inner) => this.inner = inner;
        public IReadOnlyList<PhotoInfo> Current => inner.Current;
        public IReadOnlyList<PhotoInfo> Scan(string root) { ScanCalls++; return inner.Scan(root); }
        public void StartWatching(string root, Action changed) => inner.StartWatching(root, changed);
        public void RetryWatcher() => inner.RetryWatcher();
        public void Dispose() => inner.Dispose();
    }

    private sealed class FakePreparer : IWallpaperImagePreparer
    {
        public int Calls { get; private set; }
        public List<string> AttemptPaths { get; } = [];
        public IReadOnlySet<string> LastPrunePaths { get; private set; } = new HashSet<string>();
        public Func<PhotoInfo, string?>? OnPrepare { get; set; }
        public string Prepare(PhotoInfo photo)
        {
            Calls++;
            AttemptPaths.Add(photo.Path);
            return OnPrepare?.Invoke(photo) ?? photo.Path;
        }
        public void Prune(IReadOnlySet<string> activeWallpaperPaths) => LastPrunePaths = activeWallpaperPaths;
    }

    private sealed class FakePlatform : IWallpaperPlatform
    {
        public Dictionary<string, string> Wallpapers { get; } = new() { ["wide"] = "", ["tall"] = "" };
        public List<(string Id, string Path)> Assignments { get; } = [];
        public DesktopWallpaperPosition Position { get; set; } = DesktopWallpaperPosition.Fill;
        public bool NativeSlideshow { get; set; }
        public IReadOnlyList<DisplayInfo> Displays { get; set; } =
            [new("wide", 1920, 1080), new("tall", 1080, 1920)];
        public string? FailOn { get; set; }
        public bool FailAll { get; set; }
        public Action<int>? OnGetDisplays { get; set; }
        private int getDisplaysCount;
        public IReadOnlyList<DisplayInfo> GetAttachedDisplays()
        {
            OnGetDisplays?.Invoke(++getDisplaysCount);
            return Displays;
        }
        public string GetWallpaper(string displayId) => Wallpapers[displayId];
        public DesktopWallpaperPosition GetPosition() => Position;
        public bool GetNativeSlideshowActive() => NativeSlideshow;
        public void SetWallpaper(string displayId, string path)
        {
            if (FailAll || displayId == FailOn) throw new System.Runtime.InteropServices.COMException("write failed");
            Wallpapers[displayId] = path;
            Assignments.Add((displayId, path));
        }
        public void SetPosition(DesktopWallpaperPosition position) => Position = position;
    }
    private sealed class FakeClock : IClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow; }
    public sealed class FakeTimer : IAppTimer
    {
        public TimeSpan LastDelay { get; private set; }
        public int ArmCount { get; private set; }
        public void Arm(TimeSpan delay, Action tick) { LastDelay = delay; ArmCount++; }
        public void Dispose() { }
    }
}
