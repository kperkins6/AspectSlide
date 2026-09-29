namespace AspectSlide.Model;

public sealed class SelectionHistory
{
    private readonly Dictionary<string, Queue<string>> recent = new(StringComparer.Ordinal);

    public void Record(string displayId, string photoPath)
    {
        if (!recent.TryGetValue(displayId, out var entries))
            recent[displayId] = entries = new Queue<string>();
        entries.Enqueue(photoPath);
        while (entries.Count > 20) entries.Dequeue();
    }

    public bool WasRecentlyShown(string displayId, string photoPath) =>
        recent.TryGetValue(displayId, out var entries) && entries.Contains(photoPath, StringComparer.OrdinalIgnoreCase);
}
