using System.Text.Json;

namespace AspectSlide.Slideshow;

public sealed class SettingsStore
{
    private readonly string path;
    public SettingsStore(string path) => this.path = path;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(path)) return new AppSettings();
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
            if (settings.Interval < TimeSpan.FromMinutes(1) || settings.Interval > TimeSpan.FromHours(24))
                settings.Interval = TimeSpan.FromMinutes(30);
            settings.PreviousWallpapers ??= new(StringComparer.Ordinal);
            settings.RecentPhotos ??= new(StringComparer.Ordinal);
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        string directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
