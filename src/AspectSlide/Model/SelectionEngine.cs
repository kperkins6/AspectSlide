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
        var remaining = displays.Where(d => d.Width > 0 && d.Height > 0).ToList();
        while (remaining.Count > 0)
        {
            // Assign the display with the largest loss from missing its best image first.
            var display = remaining.MaxBy(d => OpportunityCost(d, valid, used, fit, history))!;
            remaining.Remove(display);
            var candidates = CandidatePool(display, valid, used, history);
            bool allRecent = candidates.All(p => history.WasRecentlyShown(display.Id, p.Path));

            var selected = candidates
                .Select(p => (Photo: p, Cost: Cost(display, p, fit)
                    + (history.WasRecentlyShown(display.Id, p.Path) && candidates.Length > 1 ? 0.75 : 0)
                    + random.NextDouble() * (allRecent ? 0.25 : 0.000001)))
                .MinBy(item => item.Cost).Photo;
            result[display.Id] = selected;
            used.Add(selected.Path);
        }
        return result;
    }

    private static PhotoInfo[] CandidatePool(DisplayInfo display, PhotoInfo[] valid,
        HashSet<string> used, SelectionHistory history)
    {
        var unused = valid.Where(p => !used.Contains(p.Path)).ToArray();
        var candidates = unused.Length > 0 ? unused : valid;
        var matching = candidates.Where(p => SameOrientation(display, p)).ToArray();
        if (matching.Length > 0) candidates = matching;
        string? last = history.LastShown(display.Id);
        if (candidates.Length > 1 && last is not null)
        {
            var different = candidates.Where(p => !string.Equals(p.Path, last, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (different.Length > 0) candidates = different;
        }
        return candidates;
    }

    private static double OpportunityCost(DisplayInfo display, PhotoInfo[] valid,
        HashSet<string> used, WallpaperFit fit, SelectionHistory history)
    {
        var candidates = CandidatePool(display, valid, used, history);
        if (candidates.Length == 1) return double.PositiveInfinity;
        double best = double.PositiveInfinity, second = double.PositiveInfinity;
        foreach (var photo in candidates)
        {
            double cost = Cost(display, photo, fit);
            if (cost < best) { second = best; best = cost; }
            else if (cost < second) second = cost;
        }
        return second - best;
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
