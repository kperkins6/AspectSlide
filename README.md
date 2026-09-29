# AspectSlide

**A monitor-aware wallpaper slideshow for Windows 11.** Pick one local photo folder and AspectSlide changes all connected monitors on a shared interval. Portrait photos go to vertical screens, landscape photos to horizontal screens, and each monitor gets a different file when the library has enough choices.

Windows' built-in slideshow can put the same landscape image on a portrait screen, or vice versa. AspectSlide keeps the familiar folder, interval, and Fill/Fit controls while choosing a better image for each display.

## Download and use

Download the portable `AspectSlide.exe` from the [latest release](https://github.com/kperkins6/AspectSlide/releases/latest). It includes the .NET runtime, needs no installer or administrator rights, and runs on Windows 11 x64. The executable is currently unsigned.

1. Open `AspectSlide.exe`. Settings opens on manual launch.
2. Choose a local photo folder. Ordinary subfolders are included.
3. Set the interval from 1 minute to 24 hours and choose **Fill** or **Fit**.
4. Use **Change now**, **Pause**, and **Restore previous** as needed. Closing Settings keeps the tray app running; **Exit** stops it and leaves the current wallpapers visible.

**Start at sign-in** is optional and off by default. When enabled, it creates a shortcut in your user Startup folder and launches quietly after a folder has been configured. Open Settings again from the tray icon or by running the executable.

If Windows' own wallpaper slideshow is active, switch **Windows Settings → Personalization → Background** to **Picture**, then use **Check again** in AspectSlide. **Windows colors** opens the Colors page, where Windows can choose an accent color automatically from the background. Windows does not document which monitor it samples.

## How it works

- Indexes JPEG, PNG, BMP, GIF, and TIFF images in the chosen folder and subfolders. Corrupt or unavailable files are skipped.
- Prefers matching orientation, then aspect ratio and resolution. A batch reserves scarce matches for the monitors that need them, avoids immediate repeats, and shuffles among suitable images.
- Prepares selected images before changing any monitor, then assigns them through Windows' per-monitor `IDesktopWallpaper` API. Windows applies one monitor at a time, so the transition is coordinated rather than atomic.
- Uses a one-shot timer and debounced file/display notifications instead of continuous polling. The settings window generates preview thumbnails only while visible.

See [Architecture](docs/architecture.md) for the components and safety boundaries.

## Safety and limits

- Source photos are read only. Settings, metadata, and converted wallpaper images live under `%LOCALAPPDATA%\AspectSlide`.
- Network paths, folder links, file links, and cloud-only placeholders are excluded. Locally available OneDrive photos can be used.
- The original wallpaper paths and display mode are saved before the first change. **Restore previous** pauses the slideshow and restores accessible images. An original solid-color background cannot be restored automatically; the app warns before replacing one.
- Fill/Fit is shared across monitors because the Windows wallpaper position API is system-wide. The app supports one Windows virtual desktop.
- The app has no service, account, telemetry, or app-initiated network requests.

## Build and test

Install the [.NET 10 SDK](https://learn.microsoft.com/en-us/dotnet/core/install/windows) on Windows, then run:

```powershell
dotnet test AspectSlide.sln -c Release
dotnet publish src/AspectSlide/AspectSlide.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The default tests use temporary folders and fake wallpaper adapters, so they do not change your desktop. A read-only test of the real Windows COM adapter is opt-in:

```powershell
$env:ASPECTSLIDE_REAL_COM = '1'
dotnet test tests/AspectSlide.Tests/AspectSlide.Tests.csproj --filter FullyQualifiedName~WallpaperComSmokeTests
```

## License

[MIT](LICENSE) © 2026 kperkins6.
