using AspectSlide.Model;
using System.Text.Json;

namespace AspectSlide.Catalog;

public sealed record CatalogEntry(long Length, long LastWriteTicks, PhotoInfo Photo);

public sealed class CatalogCache
{
    private readonly string path;
    public CatalogCache(string path) => this.path = path;

    public Dictionary<string, CatalogEntry> Load()
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string, CatalogEntry>>(File.ReadAllText(path))
                    ?? new(StringComparer.OrdinalIgnoreCase)
                : new(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        { return new(StringComparer.OrdinalIgnoreCase); }
    }

    public void Save(Dictionary<string, CatalogEntry> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(entries));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
