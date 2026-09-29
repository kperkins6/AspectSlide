namespace AspectSlide.Model;

public sealed record PhotoInfo(string Path, int Width, int Height, int ExifOrientation = 1);
