using System.Drawing;
using System.Runtime.InteropServices;

namespace AspectSlide.Windows;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeRect { public int Left, Top, Right, Bottom; }

[ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDesktopWallpaperCom
{
    [PreserveSig] int SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId,
        [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
    [PreserveSig] int GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorId, out IntPtr wallpaper);
    [PreserveSig] int GetMonitorDevicePathAt(uint index, out IntPtr monitorId);
    [PreserveSig] int GetMonitorDevicePathCount(out uint count);
    [PreserveSig] int GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorId, out NativeRect rect);
    [PreserveSig] int SetBackgroundColor(uint color);
    [PreserveSig] int GetBackgroundColor(out uint color);
    [PreserveSig] int SetPosition(DesktopWallpaperPosition position);
    [PreserveSig] int GetPosition(out DesktopWallpaperPosition position);
    [PreserveSig] int SetSlideshow(IntPtr items);
    [PreserveSig] int GetSlideshow(out IntPtr items);
    [PreserveSig] int SetSlideshowOptions(int options, uint tick);
    [PreserveSig] int GetSlideshowOptions(out int options, out uint tick);
    [PreserveSig] int AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, int direction);
    [PreserveSig] int GetStatus(out int status);
    [PreserveSig] int Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
}

public sealed class ComWallpaperNative : IWallpaperNative, IDisposable
{
    private readonly IDesktopWallpaperCom com;
    private readonly int threadId;

    public ComWallpaperNative()
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("Wallpaper COM must be created on the UI STA thread.");
        threadId = Environment.CurrentManagedThreadId;
        Type type = Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD"), throwOnError: true)!;
        com = (IDesktopWallpaperCom)Activator.CreateInstance(type)!;
    }

    private void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != threadId)
            throw new InvalidOperationException("Wallpaper COM accessed from a different thread.");
    }
    private static void Check(int hr)
    {
        if (hr < 0) throw new COMException($"Wallpaper COM returned 0x{hr:X8}.", hr);
    }

    public IReadOnlyList<string> EnumerateMonitorIds()
    {
        CheckThread();
        Check(com.GetMonitorDevicePathCount(out uint count));
        var ids = new List<string>();
        for (uint i = 0; i < count; i++)
        {
            Check(com.GetMonitorDevicePathAt(i, out IntPtr value));
            try { ids.Add(Marshal.PtrToStringUni(value) ?? ""); }
            finally { Marshal.FreeCoTaskMem(value); }
        }
        return ids;
    }

    public Rectangle? GetMonitorRectangle(string id)
    {
        CheckThread();
        int hr = com.GetMonitorRECT(id, out var rect);
        // Some Windows 11 display drivers return E_FAIL for stale detached IDs.
        if (hr == 1 || hr == unchecked((int)0x80004005)) return null;
        Check(hr);
        return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    public string GetWallpaper(string id)
    {
        CheckThread();
        Check(com.GetWallpaper(id, out IntPtr value));
        try { return Marshal.PtrToStringUni(value) ?? ""; }
        finally { Marshal.FreeCoTaskMem(value); }
    }

    public DesktopWallpaperPosition GetPosition()
    {
        CheckThread(); Check(com.GetPosition(out var position)); return position;
    }
    public int GetStatus()
    {
        CheckThread(); Check(com.GetStatus(out int status)); return status;
    }
    public void SetWallpaper(string id, string path)
    {
        CheckThread(); Check(com.SetWallpaper(id, path));
    }
    public void SetPosition(DesktopWallpaperPosition position)
    {
        CheckThread(); Check(com.SetPosition(position));
    }
    public void Dispose()
    {
        CheckThread(); Marshal.FinalReleaseComObject(com);
    }
}
