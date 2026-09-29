using AspectSlide.Catalog;
using AspectSlide.Model;
using AspectSlide.Windows;

namespace AspectSlide.Slideshow;

public sealed class SlideshowController
{
    private readonly AppSettings settings;
    private readonly SettingsStore store;
    private readonly IPhotoCatalog catalog;
    private readonly IWallpaperImagePreparer preparer;
    private readonly IWallpaperPlatform platform;
    private readonly IClock clock;
    private readonly IAppTimer timer;
    private readonly SynchronizationContext ui;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly SelectionHistory history = new();
    private readonly object stateSync = new();
    private DateTimeOffset nextDue;
    private DateTimeOffset lastFullScan;
    private int generation;
    private int displayChangeGeneration;
    private bool automaticInProgress;
    private bool stopped;

    public string Status { get; private set; } = "Ready";
    public event Action? StatusChanged;

    public SlideshowController(AppSettings settings, SettingsStore store, IPhotoCatalog catalog,
        IWallpaperImagePreparer preparer, IWallpaperPlatform platform, IClock clock,
        IAppTimer timer, SynchronizationContext ui)
    {
        this.settings = settings;
        this.store = store;
        this.catalog = catalog;
        this.preparer = preparer;
        this.platform = platform;
        this.clock = clock;
        this.timer = timer;
        this.ui = ui;
        foreach (var (id, paths) in settings.RecentPhotos)
            foreach (string path in paths) history.Record(id, path);
    }

    public async Task StartAsync()
    {
        if (settings.Folder is null) { SetStatus("Choose a photo folder to begin."); return; }
        try
        {
            string root = LocalFolderValidator.Validate(settings.Folder);
            settings.Folder = root;
            if (!Directory.Exists(root))
            {
                catalog.StartWatching(root, () => SetStatus("Photo library updated."));
                SetStatus("Photo folder is unavailable.");
                nextDue = clock.UtcNow + TimeSpan.FromMinutes(1);
                ArmTimer();
                return;
            }
            await Task.Run(() => catalog.Scan(root));
            lastFullScan = clock.UtcNow;
            catalog.StartWatching(root, () => SetStatus("Photo library updated."));
            if (settings.Paused) { SetStatus("Paused"); return; }
            await ChangeNowAsync();
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        { SetStatus(ex.Message); }
    }

    public Task ChangeNowAsync() => ChangeAsync(manual: true);
    public Task OnTimerAsync() => RequestAutomaticAsync(force: false);
    public Task OnResumeAsync() => RequestAutomaticAsync(force: false);

    public async Task OnDisplaysChangedAsync()
    {
        int version = Interlocked.Increment(ref displayChangeGeneration);
        await Task.Delay(500);
        if (version == Volatile.Read(ref displayChangeGeneration))
            await RequestAutomaticAsync(force: true);
    }

    private async Task RequestAutomaticAsync(bool force)
    {
        lock (stateSync)
        {
            if (stopped || settings.Paused || automaticInProgress || (!force && clock.UtcNow < nextDue)) return;
            automaticInProgress = true;
        }
        try { await ChangeAsync(manual: false); }
        finally { lock (stateSync) automaticInProgress = false; }
    }

    public void SetPaused(bool paused)
    {
        lock (stateSync) { settings.Paused = paused; generation++; }
        store.Save(settings);
        SetStatus(paused ? "Paused" : "Running");
        if (!paused) ArmTimer();
    }

    public void AcknowledgeSolidColorRestoreLimitation()
    {
        settings.SolidColorAcknowledged = true;
        store.Save(settings);
    }

    public void UpdateInterval(TimeSpan interval)
    {
        if (interval < TimeSpan.FromMinutes(1) || interval > TimeSpan.FromHours(24))
            throw new ArgumentOutOfRangeException(nameof(interval));
        settings.Interval = interval;
        nextDue = clock.UtcNow + interval;
        store.Save(settings);
        ArmTimer();
    }

    private async Task ChangeAsync(bool manual)
    {
        int version;
        lock (stateSync)
        {
            if (stopped || (!manual && settings.Paused)) return;
            version = generation;
        }
        await gate.WaitAsync();
        try
        {
            lock (stateSync)
            {
                if (stopped || (!manual && (settings.Paused || version != generation))) return;
            }
            if (settings.Folder is null) { SetStatus("Choose a photo folder to begin."); return; }
            string root = LocalFolderValidator.Validate(settings.Folder);
            if (!Directory.Exists(root))
            {
                SetStatus("Photo folder is unavailable.");
                nextDue = clock.UtcNow + TimeSpan.FromMinutes(1);
                ArmTimer();
                return;
            }
            catalog.RetryWatcher();
            if (await OnUiAsync(platform.GetNativeSlideshowActive))
            { SetStatus("Windows slideshow is active. Switch Windows Background to Picture, then Check again."); return; }

            IReadOnlyList<PhotoInfo> photos = catalog.Current;
            if (photos.Count == 0 || clock.UtcNow - lastFullScan >= TimeSpan.FromDays(1))
            {
                photos = await Task.Run(() => catalog.Scan(root));
                lastFullScan = clock.UtcNow;
            }
            if (photos.Count == 0) { SetStatus("No usable photos found."); return; }
            if (!settings.OriginalCaptured)
            {
                var snapshot = await OnUiAsync(() =>
                {
                    var displays = platform.GetAttachedDisplays();
                    return (Position: platform.GetPosition(), Wallpapers: displays.ToDictionary(d => d.Id, d => platform.GetWallpaper(d.Id)));
                });
                if (!settings.SolidColorAcknowledged && snapshot.Wallpapers.Values.Any(string.IsNullOrEmpty))
                {
                    SetStatus("A monitor has a solid-color background that cannot be restored automatically. Confirm in Settings before starting.");
                    return;
                }
                settings.PreviousPosition = snapshot.Position;
                settings.PreviousWallpapers = snapshot.Wallpapers;
                settings.OriginalCaptured = true;
                store.Save(settings);
            }

            var rejected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int topologyAttempt = 0; topologyAttempt < 3; topologyAttempt++)
            {
                var displays = await OnUiAsync(platform.GetAttachedDisplays);
                if (displays.Count == 0) { SetStatus("No attached monitors found."); return; }
                IReadOnlyDictionary<string, PhotoInfo> chosen;
                Dictionary<string, string> prepared;
                while (true)
                {
                    var available = photos.Where(p => !rejected.Contains(p.Path)).ToArray();
                    if (available.Length == 0) { SetStatus("No usable photos remain in this session."); return; }
                    chosen = SelectionEngine.Choose(displays, available, settings.Fit, history, Random.Shared);
                    prepared = new Dictionary<string, string>(StringComparer.Ordinal);
                    bool failed = false;
                    foreach (var (id, photo) in chosen)
                    {
                        try { prepared[id] = await Task.Run(() => preparer.Prepare(photo)); }
                        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or OutOfMemoryException)
                        { rejected.Add(photo.Path); failed = true; break; }
                    }
                    if (!failed) break;
                }
                lock (stateSync)
                {
                    if (stopped || (!manual && (settings.Paused || version != generation))) return;
                }
                var fresh = await OnUiAsync(platform.GetAttachedDisplays);
                if (!displays.SequenceEqual(fresh)) continue;

                var failedDisplays = new List<string>();
                await OnUiAsync(() =>
                {
                    platform.SetPosition(settings.Fit == WallpaperFit.Fill ? DesktopWallpaperPosition.Fill : DesktopWallpaperPosition.Fit);
                    foreach (var (id, photo) in chosen)
                    {
                        try
                        {
                            platform.SetWallpaper(id, prepared[id]);
                            history.Record(id, photo.Path);
                            if (!settings.RecentPhotos.TryGetValue(id, out var recent))
                                settings.RecentPhotos[id] = recent = [];
                            recent.Add(photo.Path);
                            if (recent.Count > 20) recent.RemoveAt(0);
                        }
                        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException)
                        { failedDisplays.Add(id); }
                    }
                });
                store.Save(settings);
                try
                {
                    var activePaths = await OnUiAsync(() => platform.GetAttachedDisplays()
                        .Select(d => platform.GetWallpaper(d.Id)).Where(p => !string.IsNullOrEmpty(p))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase));
                    await Task.Run(() => preparer.Prune(activePaths));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
                { /* An incomplete active-path list must never drive cache deletion. */ }
                nextDue = clock.UtcNow + settings.Interval;
                ArmTimer();
                SetStatus(failedDisplays.Count == 0 ? $"Changed {chosen.Count} wallpaper(s)." :
                    "Could not update monitor(s): " + string.Join(", ", failedDisplays));
                return;
            }
            SetStatus("Displays changed during update; waiting for the next event.");
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        { SetStatus(ex.Message); }
        finally { gate.Release(); }
    }

    public async Task RestorePreviousAsync()
    {
        SetPaused(true);
        await gate.WaitAsync();
        try
        {
            if (!settings.OriginalCaptured) { SetStatus("No previous wallpapers were saved."); return; }
            var skipped = new List<string>();
            await OnUiAsync(() =>
            {
                platform.SetPosition(settings.PreviousPosition);
                var attached = platform.GetAttachedDisplays().Select(d => d.Id).ToHashSet();
                foreach (var (id, path) in settings.PreviousWallpapers)
                {
                    if (string.IsNullOrEmpty(path) || !File.Exists(path) || !attached.Contains(id))
                    { skipped.Add(id); continue; }
                    platform.SetWallpaper(id, path);
                }
            });
            SetStatus(skipped.Count == 0 ? "Previous wallpapers restored." :
                "Some previous wallpapers could not be restored: " + string.Join(", ", skipped));
        }
        finally { gate.Release(); }
    }

    private void ArmTimer()
    {
        if (stopped || settings.Paused) return;
        TimeSpan delay = nextDue - clock.UtcNow;
        if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
        timer.Arm(delay, () => _ = OnTimerAsync());
    }

    private Task OnUiAsync(Action action) => OnUiAsync(() => { action(); return true; });
    private Task<T> OnUiAsync<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        ui.Post(_ =>
        {
            try { completion.SetResult(action()); }
            catch (Exception ex) { completion.SetException(ex); }
        }, null);
        return completion.Task;
    }

    private void SetStatus(string status) { Status = status; StatusChanged?.Invoke(); }
    public void Stop()
    {
        lock (stateSync) { stopped = true; generation++; }
        Interlocked.Increment(ref displayChangeGeneration);
        timer.Dispose();
        catalog.Dispose();
    }
}
