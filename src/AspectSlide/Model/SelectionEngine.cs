namespace AspectSlide.Model;

public static class SelectionEngine
{
    public static IReadOnlyDictionary<string, PhotoInfo> Choose(
        IReadOnlyList<DisplayInfo> displays, IReadOnlyList<PhotoInfo> photos,
        WallpaperFit fit, SelectionHistory history, Random random)
    {
        var result = new Dictionary<string, PhotoInfo>(StringComparer.Ordinal);
        var valid = photos.Where(p => p.Width > 0 && p.Height > 0 && !string.IsNullOrWhiteSpace(p.Path)).ToArray();
        if (valid.Length == 0) return result;

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var display in displays.Where(d => d.Width > 0 && d.Height > 0)
                     .OrderBy(d => valid.Count(p => SameOrientation(d, p))))
        {
            var unused = valid.Where(p => !used.Contains(p.Path)).ToArray();
            var candidates = unused.Length > 0 ? unused : valid;
            var matching = candidates.Where(p => SameOrientation(display, p)).ToArray();
            if (matching.Length > 0) candidates = matching;

            var selected = candidates
                .Select(p => (Photo: p, Cost: Cost(display, p, fit)
                    + (history.WasRecentlyShown(display.Id, p.Path) && candidates.Length > 1 ? 0.75 : 0)
                    + random.NextDouble() * 0.000001))
                .MinBy(item => item.Cost).Photo;
            result[display.Id] = selected;
            used.Add(selected.Path);
        }
        return result;
    }

    private static bool SameOrientation(DisplayInfo display, PhotoInfo photo) =>
        (display.Width > display.Height && photo.Width > photo.Height) ||
        (display.Height > display.Width && photo.Height > photo.Width);

    private static double Cost(DisplayInfo display, PhotoInfo photo, WallpaperFit fit)
    {
        double ratioCost = Math.Abs(Math.Log(((double)photo.Width / photo.Height) /
                                             ((double)display.Width / display.Height)));
        double widthScale = (double)display.Width / photo.Width;
        double heightScale = (double)display.Height / photo.Height;
        double scale = fit == WallpaperFit.Fill ? Math.Max(widthScale, heightScale) : Math.Min(widthScale, heightScale);
        return ratioCost + (scale > 1 ? Math.Log(scale) * 0.25 : 0);
    }
}
