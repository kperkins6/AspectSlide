using AspectSlide.UI;
using System.Runtime.InteropServices;

namespace AspectSlide;

internal static class Program
{
    internal const string ShowMessageName = "AspectSlide.ShowSettings.54F4C0E6";
    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode)]
    internal static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    private static readonly IntPtr Broadcast = new(0xffff);

    [STAThread]
    private static void Main(string[] args)
    {
        bool background = args.Contains(StartupShortcut.BackgroundArgument, StringComparer.OrdinalIgnoreCase);
        using var single = new Mutex(initiallyOwned: true, "Local\\AspectSlide.OneInstance.54F4C0E6", out bool first);
        if (!first)
        {
            if (!background) PostMessage(Broadcast, RegisterWindowMessage(ShowMessageName), IntPtr.Zero, IntPtr.Zero);
            return;
        }
        ApplicationConfiguration.Initialize();
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        using var app = new TrayContext(background);
        Application.Run(app);
    }
}
