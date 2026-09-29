using AspectSlide.Model;

namespace AspectSlide.Catalog;

public sealed class PhotoCatalog : IPhotoCatalog
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp" };
    private readonly IPhotoProbe probe;
    private readonly CatalogCache? cache;
    private Dictionary<string, CatalogEntry> entries;
    private readonly object sync = new();
    private readonly object scanSync = new();
    private FileSystemWatcher? watcher;
    private System.Threading.Timer? debounce;
    private string? root;
    private Action? changed;
    private bool disposed;
    private IReadOnlyList<PhotoInfo> current = [];

    public PhotoCatalog(IPhotoProbe probe, CatalogCache? cache = null)
    {
        this.probe = probe;
        this.cache = cache;
        entries = cache?.Load() ?? new(StringComparer.OrdinalIgnoreCase);
    }
    public IReadOnlyList<PhotoInfo> Current { get { lock (sync) return current; } }
    public static bool ShouldTraverse(FileAttributes attributes, string? linkTarget) =>
        linkTarget is null &&
        (attributes & (FileAttributes.Offline | (FileAttributes)0x00040000 | (FileAttributes)0x00400000)) == 0;

    public IReadOnlyList<PhotoInfo> Scan(string path)
    {
        lock (scanSync) return ScanCore(path);
    }

    private IReadOnlyList<PhotoInfo> ScanCore(string path)
    {
        string full = LocalFolderValidator.Validate(path);
        if (!Directory.Exists(full)) return Current;
        var found = new List<PhotoInfo>();
        var pending = new Stack<string>();
        pending.Push(full);
        while (pending.TryPop(out string? directory))
        {
            try
            {
                foreach (string child in Directory.EnumerateFileSystemEntries(directory))
                {
                    try
                    {
                        FileAttributes attributes = File.GetAttributes(child);
                        string? linkTarget = null;
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                            linkTarget = (attributes & FileAttributes.Directory) != 0
                                ? new DirectoryInfo(child).LinkTarget
                                : new FileInfo(child).LinkTarget;
                        if (!ShouldTraverse(attributes, linkTarget)) continue;
                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            pending.Push(child);
                            continue;
                        }
                        if (!Extensions.Contains(Path.GetExtension(child))) continue;
                        var info = new FileInfo(child);
                        if (entries.TryGetValue(child, out var cached) &&
                            cached.Length == info.Length && cached.LastWriteTicks == info.LastWriteTimeUtc.Ticks)
                        {
                            found.Add(cached.Photo);
                        }
                        else if (probe.TryRead(child, out var photo))
                        {
                            found.Add(photo);
                            entries[child] = new CatalogEntry(info.Length, info.LastWriteTimeUtc.Ticks, photo);
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                    { /* Skip this entry. */ }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { /* Skip this directory. */ }
        }
        lock (sync) current = found;
        if (cache is not null)
        {
            var paths = found.Select(p => p.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var key in entries.Keys.Where(k => !paths.Contains(k)).ToArray()) entries.Remove(key);
            try { cache.Save(entries); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return found;
    }

    public void StartWatching(string path, Action onChanged)
    {
        string full = LocalFolderValidator.Validate(path);
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            watcher?.Dispose();
            debounce?.Dispose();
            watcher = null;
            debounce = null;
            root = full;
            changed = onChanged;
            if (!Directory.Exists(full)) return;
            watcher = new FileSystemWatcher(full) { IncludeSubdirectories = true, EnableRaisingEvents = true };
            watcher.Changed += WatcherChanged;
            watcher.Created += WatcherChanged;
            watcher.Deleted += WatcherChanged;
            watcher.Renamed += WatcherChanged;
            watcher.Error += (_, _) => ScheduleRefresh();
        }
    }

    public void RetryWatcher()
    {
        string? path;
        Action? callback;
        lock (sync)
        {
            if (watcher is { EnableRaisingEvents: true }) return;
            path = root; callback = changed;
        }
        if (path is not null && callback is not null && Directory.Exists(path)) StartWatching(path, callback);
    }

    private void WatcherChanged(object sender, FileSystemEventArgs args) => ScheduleRefresh();
    private void ScheduleRefresh()
    {
        lock (sync)
        {
            if (disposed) return;
            debounce ??= new System.Threading.Timer(_ =>
            {
                string? path;
                Action? callback;
                lock (sync) { path = root; callback = changed; }
                if (path is null) return;
                try { Scan(path); callback?.Invoke(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
            });
            debounce.Change(500, Timeout.Infinite);
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            disposed = true;
            root = null;
            changed = null;
            watcher?.Dispose();
            debounce?.Dispose();
            watcher = null;
            debounce = null;
        }
    }
}
