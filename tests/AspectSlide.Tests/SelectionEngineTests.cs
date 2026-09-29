using AspectSlide.Model;

namespace AspectSlide.Tests;

public sealed class SelectionEngineTests
{
    [Fact]
    public void AssignsDistinctOrientationMatchesToMixedMonitors()
    {
        var displays = new[] { new DisplayInfo("wide", 3440, 1440), new DisplayInfo("tall", 1080, 1920) };
        var photos = new[] { new PhotoInfo("wide.jpg", 3440, 1440), new PhotoInfo("tall.jpg", 1080, 1920) };

        var chosen = SelectionEngine.Choose(displays, photos, WallpaperFit.Fill, new SelectionHistory(), new Random(1));

        Assert.Equal("wide.jpg", chosen["wide"].Path);
        Assert.Equal("tall.jpg", chosen["tall"].Path);
    }

    [Fact]
    public void UsesDistinctPhotosForTwoLandscapeMonitors()
    {
        var displays = new[] { new DisplayInfo("a", 1920, 1080), new DisplayInfo("b", 1920, 1080) };
        var photos = new[] { new PhotoInfo("one.jpg", 1920, 1080), new PhotoInfo("two.jpg", 1920, 1080) };

        var chosen = SelectionEngine.Choose(displays, photos, WallpaperFit.Fill, new SelectionHistory(), new Random(2));

        Assert.Equal(2, chosen.Values.Select(p => p.Path).Distinct().Count());
    }

    [Fact]
    public void EmptyLibraryChangesNothingAndOnePhotoCanBeReused()
    {
        var displays = new[] { new DisplayInfo("a", 1920, 1080), new DisplayInfo("b", 1080, 1920) };
        Assert.Empty(SelectionEngine.Choose(displays, [], WallpaperFit.Fill, new SelectionHistory(), new Random(1)));

        var chosen = SelectionEngine.Choose(displays, [new PhotoInfo("only.jpg", 1000, 1000)], WallpaperFit.Fill, new SelectionHistory(), new Random(1));
        Assert.Equal("only.jpg", chosen["a"].Path);
        Assert.Equal("only.jpg", chosen["b"].Path);
    }

    [Fact]
    public void FallsBackToClosestAspectWhenNoPortraitPhotoExists()
    {
        var display = new[] { new DisplayInfo("portrait", 1080, 1920) };
        var photos = new[] { new PhotoInfo("square.jpg", 2000, 2000), new PhotoInfo("wide.jpg", 3840, 1080) };

        var chosen = SelectionEngine.Choose(display, photos, WallpaperFit.Fill, new SelectionHistory(), new Random(1));

        Assert.Equal("square.jpg", chosen["portrait"].Path);
    }

    [Fact]
    public void GlobalAssignmentReservesUltrawidePhotoForUltrawideMonitor()
    {
        var displays = new[] { new DisplayInfo("ordinary", 1920, 1080), new DisplayInfo("ultrawide", 3440, 1440) };
        var photos = new[] { new PhotoInfo("ultrawide.jpg", 3440, 1440), new PhotoInfo("compact.jpg", 2400, 2000) };

        var chosen = SelectionEngine.Choose(displays, photos, WallpaperFit.Fill, new SelectionHistory(), new Random(1));

        Assert.Equal("ultrawide.jpg", chosen["ultrawide"].Path);
        Assert.Equal("compact.jpg", chosen["ordinary"].Path);
    }

    [Fact]
    public void FillAndFitAccountForDifferentRequiredScale()
    {
        var display = new[] { new DisplayInfo("wide", 2000, 1000) };
        var photos = new[] { new PhotoInfo("exact-small.jpg", 1600, 800), new PhotoInfo("slightly-tall.jpg", 2010, 955) };

        var fill = SelectionEngine.Choose(display, photos, WallpaperFit.Fill, new SelectionHistory(), new Random(1));
        var fit = SelectionEngine.Choose(display, photos, WallpaperFit.Fit, new SelectionHistory(), new Random(1));

        Assert.Equal("exact-small.jpg", fill["wide"].Path);
        Assert.Equal("slightly-tall.jpg", fit["wide"].Path);
    }

    [Fact]
    public void TwoImageLibraryDoesNotRepeatTheCurrentImage()
    {
        var display = new[] { new DisplayInfo("wide", 1920, 1080) };
        var photos = new[] { new PhotoInfo("exact.jpg", 1920, 1080), new PhotoInfo("close.jpg", 1920, 1200) };
        var history = new SelectionHistory();
        string? previous = null;
        for (int i = 0; i < 25; i++)
        {
            string current = SelectionEngine.Choose(display, photos, WallpaperFit.Fill, history, new Random(i))["wide"].Path;
            Assert.NotEqual(previous, current);
            history.Record("wide", current);
            previous = current;
        }
    }

    [Fact]
    public void ShuffleContinuesToVaryAfterAllSuitableImagesEnterHistory()
    {
        var display = new[] { new DisplayInfo("wide", 1920, 1080) };
        var photos = new[]
        {
            new PhotoInfo("exact.jpg", 1920, 1080),
            new PhotoInfo("near.jpg", 2000, 1100),
            new PhotoInfo("other.jpg", 1920, 1000)
        };
        var history = new SelectionHistory();
        foreach (var photo in photos) history.Record("wide", photo.Path);
        var picks = new HashSet<string>();

        for (int i = 0; i < 10; i++)
        {
            string path = SelectionEngine.Choose(display, photos, WallpaperFit.Fill, history, new Random(i))["wide"].Path;
            picks.Add(path);
            history.Record("wide", path);
        }

        Assert.Equal(3, picks.Count);
    }
}
