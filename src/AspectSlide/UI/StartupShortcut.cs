using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace AspectSlide.UI;

public sealed class StartupShortcut
{
    public const string BackgroundArgument = "--background";
    private readonly string path;
    private readonly string executable;
    public StartupShortcut(string startupDirectory, string executablePath)
    {
        path = Path.Combine(startupDirectory, "AspectSlide.lnk");
        executable = Path.GetFullPath(executablePath);
    }

    public bool IsEnabled => File.Exists(path) && IsOwnLink();

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            bool exists = File.Exists(path);
            if (exists && !IsOwnLink()) throw new IOException("A different shortcut already uses AspectSlide.lnk.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var link = CreateLink();
            try
            {
                if (exists) ((IPersistFile)link).Load(path, 0);
                link.SetPath(executable);
                link.SetWorkingDirectory(Path.GetDirectoryName(executable)!);
                link.SetArguments(BackgroundArgument);
                ((IPersistFile)link).Save(path, true);
            }
            finally { Marshal.FinalReleaseComObject(link); }
        }
        else if (IsEnabled) File.Delete(path);
    }

    private bool IsOwnLink()
    {
        try
        {
            var link = CreateLink();
            try
            {
                ((IPersistFile)link).Load(path, 0);
                var buffer = new StringBuilder(32768);
                link.GetPath(buffer, buffer.Capacity, IntPtr.Zero, 0);
                return string.Equals(Path.GetFullPath(buffer.ToString()), executable, StringComparison.OrdinalIgnoreCase);
            }
            finally { Marshal.FinalReleaseComObject(link); }
        }
        catch (Exception ex) when (ex is COMException or IOException or ArgumentException) { return false; }
    }

    private static IShellLinkW CreateLink()
    {
        Type type = Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"), true)!;
        return (IShellLinkW)Activator.CreateInstance(type)!;
    }
}

[ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellLinkW
{
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxPath, IntPtr findData, int flags);
    void GetIDList(out IntPtr idList);
    void SetIDList(IntPtr idList);
    void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxName);
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
    void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int maxPath);
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
    void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int maxArgs);
    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
    void GetHotkey(out short hotkey);
    void SetHotkey(short hotkey);
    void GetShowCmd(out int showCmd);
    void SetShowCmd(int showCmd);
    void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int maxPath, out int iconIndex);
    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, int reserved);
    void Resolve(IntPtr window, int flags);
    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
}
