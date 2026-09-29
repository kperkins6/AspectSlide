using AspectSlide.Model;
using System.Drawing;

namespace AspectSlide.Catalog;

public sealed class ImagePhotoProbe : IPhotoProbe
{
    public bool TryRead(string path, out PhotoInfo photo)
    {
        photo = null!;
        try
        {
            using var image = Image.FromFile(path);
            int orientation = ReadOrientation(image);
            bool turn = orientation is >= 5 and <= 8;
            photo = new PhotoInfo(path, turn ? image.Height : image.Width,
                turn ? image.Width : image.Height, orientation);
            return photo.Width > 0 && photo.Height > 0;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or OutOfMemoryException)
        {
            return false;
        }
    }

    internal static int ReadOrientation(Image image)
    {
        const int orientationId = 0x0112;
        if (!image.PropertyIdList.Contains(orientationId)) return 1;
        byte[] value = image.GetPropertyItem(orientationId)?.Value ?? [];
        return value.Length >= 2 ? BitConverter.ToUInt16(value) : 1;
    }
}
