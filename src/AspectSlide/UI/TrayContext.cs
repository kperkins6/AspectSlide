using AspectSlide.Catalog;
using AspectSlide.Slideshow;
using AspectSlide.Windows;
using Microsoft.Win32;
using System.Diagnostics;
using System.Drawing;

namespace AspectSlide.UI;

public sealed class TrayContext : ApplicationContext
{
    private readonly ComWallpaperNative native;
    private readonly DesktopWallpaperPlatform platform;
    private readonly SettingsStore store;
    private readonly AppSettings settings;
    private readonly SlideshowController controller;
    private readonly StartupShortcut startup;
    private readonly NotifyIcon tray;
    private readonly MessageWindow window;
    private SettingsForm? form;
    private bool disposed;

    public TrayContext()
    {
        string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AspectSlide");
        store = new SettingsStore(Path.Combine(data, "settings.json"));
        settings = store.Load();
        native = new ComWallpaperNative();
        platform = new DesktopWallpaperPlatform(native);
        startup = new StartupShortcut(Environment.GetFolderPath(Environment.SpecialFolder.Startup), Application.ExecutablePath);
        controller = new SlideshowController(settings, store,
            new PhotoCatalog(new ImagePhotoProbe(), new CatalogCache(Path.Combine(data, "catalog.json"))),
            new WallpaperImageCache(Path.Combine(data, "wallpaper-cache")), platform,
            new SystemClock(), new SystemAppTimer(), SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext());
        controller.StatusChanged += OnStatusChanged;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Settings", null, (_, _) => ShowSettings());
        menu.Items.Add("Change now", null, async (_, _) =>
        {
            if (!settings.OriginalCaptured && !settings.SolidColorAcknowledged &&
                platform.GetAttachedDisplays().Any(d => string.IsNullOrEmpty(platform.GetWallpaper(d.Id))))
            { ShowSettings(); return; }
            await controller.ChangeNowAsync();
        });
        menu.Items.Add("Pause / resume", null, (_, _) => controller.SetPaused(!settings.Paused));
        menu.Items.Add("Restore previous wallpapers", null, async (_, _) => await controller.RestorePreviousAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "AspectSlide", Visible = true, ContextMenuStrip = menu };
        tray.DoubleClick += (_, _) => ShowSettings();
        window = new MessageWindow(() => _ = controller.OnDisplaysChangedAsync(), ShowSettings);
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        _ = BeginStartAsync();
    }

    private async Task BeginStartAsync()
    {
        try { await controller.StartAsync(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "AspectSlide", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs args)
    {
        if (args.Mode == PowerModes.Resume) _ = controller.OnResumeAsync();
    }

    private void OnStatusChanged()
    {
        if (form is { IsDisposed: false })
        {
            if (form.InvokeRequired) form.BeginInvoke(() => form.RefreshStatus());
            else form.RefreshStatus();
        }
    }

    private void ShowSettings()
    {
        if (form is null || form.IsDisposed)
            form = new SettingsForm(settings, store, controller, startup, platform, ExitThread);
        form.Show();
        form.WindowState = FormWindowState.Normal;
        form.BringToFront();
        form.Activate();
    }

    protected override void ExitThreadCore()
    {
        if (disposed) return;
        disposed = true;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        controller.StatusChanged -= OnStatusChanged;
        controller.Stop();
        window.Dispose();
        form?.Dispose();
        tray.Visible = false;
        tray.Dispose();
        native.Dispose();
        base.ExitThreadCore();
    }

    private sealed class MessageWindow : NativeWindow, IDisposable
    {
        private readonly Action displaysChanged;
        private readonly Action showSettings;
        private readonly uint showMessage = Program.RegisterWindowMessage(Program.ShowMessageName);
        public MessageWindow(Action displaysChanged, Action showSettings)
        {
            this.displaysChanged = displaysChanged;
            this.showSettings = showSettings;
            CreateHandle(new CreateParams { Caption = "AspectSlide messages" });
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x007E) displaysChanged(); // WM_DISPLAYCHANGE
            else if ((uint)message.Msg == showMessage) showSettings();
            base.WndProc(ref message);
        }
        public void Dispose() => DestroyHandle();
    }
}
