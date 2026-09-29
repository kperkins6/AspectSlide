using AspectSlide.Slideshow;

namespace AspectSlide.UI;

public static class LaunchPresentation
{
    public static bool NeedsSettingsWindow(AppSettings settings, bool background) =>
        !background || string.IsNullOrWhiteSpace(settings.Folder);
}
