using AspectSlide.Catalog;
using AspectSlide.Model;
using System.Drawing;
using System.Security.Cryptography;

namespace AspectSlide.Tests;

public sealed class PhotoCatalogTests
{
    [Fact]
    public void RecursesOrdinaryFoldersAndSkipsBadImages()
    {
        using var folder = new TemporaryFolder();
        Directory.CreateDirectory(System.IO.Path.Combine(folder.Path, "nested"));
        File.WriteAllText(System.IO.Path.Combine(folder.Path, "a.jpg"), "a");
        File.WriteAllText(System.IO.Path.Combine(folder.Path, "nested", "b.png"), "b");
        File.WriteAllText(System.IO.Path.Combine(folder.Path, "nested", "bad.jpg"), "bad");
        var catalog = new PhotoCatalog(new FakeProbe());

        var result = catalog.Scan(folder.Path).Select(p => System.IO.Path.GetFileName(p.Path)).Order().ToArray();

        Assert.Equal(["a.jpg", "b.png"], result);
    }

    [Fact]
    public void MissingRootKeepsLastGoodSnapshot()
    {
        using var folder = new TemporaryFolder();
        File.WriteAllText(System.IO.Path.Combine(folder.Path, "a.jpg"), "a");
        var catalog = new PhotoCatalog(new FakeProbe());
        catalog.Scan(folder.Path);

        catalog.Scan(System.IO.Path.Combine(folder.Path, "missing"));

        Assert.Single(catalog.Current);
    }

    [Fact]
    public void RejectsUncRootsBeforeOpeningThem()
    {
        Assert.Throws<ArgumentException>(() => LocalFolderValidator.Validate(@"\\server\share\photos"));
    }

    [Fact]
    public void RejectsRelativePersistedFolderPath()
    {
        Assert.Throws<ArgumentException>(() => LocalFolderValidator.Validate("photos"));
    }

    [Fact]
    public void NeverTraversesDirectoryLinks()
    {
        Assert.False(PhotoCatalog.ShouldTraverse(FileAttributes.Directory | FileAttributes.ReparsePoint, "target"));
    }

    [Fact]
    public void SkipsCloudRecallPlaceholdersBeforeReadingThem()
    {
        Assert.False(PhotoCatalog.ShouldTraverse((FileAttributes)0x00400000, null));
        Assert.False(PhotoCatalog.ShouldTraverse((FileAttributes)0x00040000, null));
        Assert.True(PhotoCatalog.ShouldTraverse(FileAttributes.ReparsePoint, null));
    }

    [Fact]
    public void LocallyAvailableOneDriveRootIsNotTreatedAsADirectoryLink()
    {
        string oneDrive = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive");
        if (!Directory.Exists(oneDrive) || new DirectoryInfo(oneDrive).LinkTarget is not null) return;
        Assert.Equal(oneDrive, LocalFolderValidator.Validate(oneDrive));
    }

    [Fact]
    public void MetadataCacheAvoidsReprobingUnchangedPhotos()
    {
        using var folder = new TemporaryFolder();
        string image = System.IO.Path.Combine(folder.Path, "a.jpg");
        File.WriteAllText(image, "a");
        string cachePath = System.IO.Path.Combine(folder.Path, "metadata.json");
        var probe = new CountingProbe();
        new PhotoCatalog(probe, new CatalogCache(cachePath)).Scan(folder.Path);
        new PhotoCatalog(probe, new CatalogCache(cachePath)).Scan(folder.Path);
        Assert.Equal(1, probe.Calls);

        File.AppendAllText(image, "more");
        new PhotoCatalog(probe, new CatalogCache(cachePath)).Scan(folder.Path);
        Assert.Equal(2, probe.Calls);
    }

    [Fact]
    public void ExifRotationIsReflectedInDimensionsAndCachedWallpaper()
    {
        using var folder = new TemporaryFolder();
        string source = System.IO.Path.Combine(folder.Path, "turned.jpg");
        using (var bitmap = new Bitmap(300, 200)) bitmap.Save(source, System.Drawing.Imaging.ImageFormat.Jpeg);
        byte[] original = File.ReadAllBytes(source);
        byte[] exif = [0xFF, 0xE1, 0x00, 0x22, 0x45, 0x78, 0x69, 0x66, 0, 0,
            0x49, 0x49, 0x2A, 0, 0x08, 0, 0, 0, 0x01, 0,
            0x12, 0x01, 0x03, 0, 0x01, 0, 0, 0, 0x06, 0, 0, 0, 0, 0, 0, 0];
        File.WriteAllBytes(source, original[..2].Concat(exif).Concat(original[2..]).ToArray());
        byte[] before = SHA256.HashData(File.ReadAllBytes(source));
        var probe = new ImagePhotoProbe();

        Assert.True(probe.TryRead(source, out var photo));
        Assert.Equal((200, 300, 6), (photo.Width, photo.Height, photo.ExifOrientation));
        var cache = new WallpaperImageCache(System.IO.Path.Combine(folder.Path, "cache"));
        string prepared = cache.Prepare(photo);

        Assert.NotEqual(source, prepared);
        using (var rotated = Image.FromFile(prepared)) Assert.Equal((200, 300), (rotated.Width, rotated.Height));
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(source)));
        Assert.Equal(prepared, cache.Prepare(photo));
    }

    [Fact]
    public void SelectedSourceDeletedAfterIndexingIsRejectedBeforeApplyingBatch()
    {
        using var folder = new TemporaryFolder();
        string source = System.IO.Path.Combine(folder.Path, "gone.jpg");
        File.WriteAllText(source, "gone");
        var cache = new WallpaperImageCache(System.IO.Path.Combine(folder.Path, "cache"));
        File.Delete(source);

        Assert.Throws<FileNotFoundException>(() => cache.Prepare(new PhotoInfo(source, 100, 50)));
    }

    private sealed class FakeProbe : IPhotoProbe
    {
        public bool TryRead(string path, out PhotoInfo photo)
        {
            photo = new PhotoInfo(path, 100, 50);
            return !path.Contains("bad", StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class CountingProbe : IPhotoProbe
    {
        public int Calls { get; private set; }
        public bool TryRead(string path, out PhotoInfo photo)
        {
            Calls++;
            photo = new PhotoInfo(path, 100, 50);
            return true;
        }
    }
}

internal sealed class TemporaryFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AspectSlideTests-" + Guid.NewGuid().ToString("N"));
    public TemporaryFolder() => Directory.CreateDirectory(Path);
    public void Dispose() => Directory.Delete(Path, recursive: true);
}
