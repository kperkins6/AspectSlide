using AspectSlide.Model;

namespace AspectSlide.Slideshow;

public interface IWallpaperImagePreparer
{
    string Prepare(PhotoInfo photo);
    void Prune(IReadOnlySet<string> activeWallpaperPaths);
}
