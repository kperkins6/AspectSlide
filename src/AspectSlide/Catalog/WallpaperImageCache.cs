using AspectSlide.Model;
using System.Drawing;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text;
using AspectSlide.Slideshow;

namespace AspectSlide.Catalog;

public sealed class WallpaperImageCache : IWallpaperImagePreparer
{
    private readonly string directory;
    private readonly long maxBytes;

    public WallpaperImageCache(string directory, long maxBytes = 256 * 1024 * 1024)
    {
        this.directory = directory;
        this.maxBytes = maxBytes;
    }

    public string Prepare(PhotoInfo photo)
    {
        if (!File.Exists(photo.Path)) throw new FileNotFoundException("Selected photo is no longer available.", photo.Path);
        FileAttributes attributes = File.GetAttributes(photo.Path);
        string? linkTarget = (attributes & FileAttributes.ReparsePoint) != 0 ? new FileInfo(photo.Path).LinkTarget : null;
        if (!PhotoCatalog.ShouldTraverse(attributes, linkTarget))
            throw new IOException("Selected photo is not locally available.");
        string extension = Path.GetExtension(photo.Path);
        if (photo.ExifOrientation == 1 &&
            (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
             extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
             extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
             extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase)))
            return photo.Path;

        var info = new FileInfo(photo.Path);
        string key = $"{Path.GetFullPath(photo.Path)}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        string name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".jpg";
        Directory.CreateDirectory(directory);
        string destination = Path.Combine(directory, name);
        if (File.Exists(destination)) return destination;

        string temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var source = Image.FromFile(photo.Path))
            using (var bitmap = new Bitmap(source))
            {
                bitmap.RotateFlip(ToRotation(photo.ExifOrientation));
                bitmap.Save(temporary, ImageFormat.Jpeg);
            }
            File.Move(temporary, destination, overwrite: true);
            return destination;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public void Prune(IReadOnlySet<string> activeWallpaperPaths)
    {
        if (!Directory.Exists(directory)) return;
        var files = new DirectoryInfo(directory).EnumerateFiles("*.jpg", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f.LastWriteTimeUtc).ToArray();
        long total = files.Sum(f => f.Length);
        foreach (var file in files)
        {
            if (total <= maxBytes) break;
            if (activeWallpaperPaths.Contains(file.FullName)) continue;
            long length = file.Length;
            file.Delete();
            total -= length;
        }
    }

    private static RotateFlipType ToRotation(int orientation) => orientation switch
    {
        2 => RotateFlipType.RotateNoneFlipX,
        3 => RotateFlipType.Rotate180FlipNone,
        4 => RotateFlipType.RotateNoneFlipY,
        5 => RotateFlipType.Rotate90FlipX,
        6 => RotateFlipType.Rotate90FlipNone,
        7 => RotateFlipType.Rotate270FlipX,
        8 => RotateFlipType.Rotate270FlipNone,
        _ => RotateFlipType.RotateNoneFlipNone
    };
}
