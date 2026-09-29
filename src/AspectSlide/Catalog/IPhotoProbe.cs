using AspectSlide.Model;

namespace AspectSlide.Catalog;

public interface IPhotoProbe
{
    bool TryRead(string path, out PhotoInfo photo);
}
