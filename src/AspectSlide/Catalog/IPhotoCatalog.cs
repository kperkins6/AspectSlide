using AspectSlide.Model;

namespace AspectSlide.Catalog;

public interface IPhotoCatalog : IDisposable
{
    IReadOnlyList<PhotoInfo> Current { get; }
    IReadOnlyList<PhotoInfo> Scan(string root);
    void StartWatching(string root, Action changed);
    void RetryWatcher();
}
