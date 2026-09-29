namespace AspectSlide.Catalog;

public static class LocalFolderValidator
{
    public static string Validate(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("Choose a local folder.", nameof(root));
        if (root.StartsWith(@"\\", StringComparison.Ordinal))
            throw new ArgumentException("Network and device paths are not supported.", nameof(root));
        if (!Path.IsPathFullyQualified(root))
            throw new ArgumentException("Choose an absolute local folder path.", nameof(root));

        string full = Path.GetFullPath(root);
        string? driveRoot = Path.GetPathRoot(full);
        if (driveRoot is null || new DriveInfo(driveRoot).DriveType == DriveType.Network)
            throw new ArgumentException("Choose a local drive.", nameof(root));

        string current = driveRoot;
        string tail = full[driveRoot.Length..];
        foreach (string part in tail.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            if (!Directory.Exists(current)) continue;
            var attributes = File.GetAttributes(current);
            if ((attributes & (FileAttributes.Offline | (FileAttributes)0x00040000 | (FileAttributes)0x00400000)) != 0)
                throw new ArgumentException("Choose a locally available folder.", nameof(root));
            if ((attributes & FileAttributes.ReparsePoint) != 0 &&
                new DirectoryInfo(current).LinkTarget is not null)
                throw new ArgumentException("Folder links and junctions are not supported.", nameof(root));
        }
        return full;
    }
}
