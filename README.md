# AspectSlide

AspectSlide is a small Windows 11 tray app that changes wallpapers on connected monitors together. It chooses a different image for each monitor when enough photos are available, favoring orientation, aspect ratio, and resolution. It supports landscape, portrait, and ultrawide displays in one setup.

## Use

1. Run `AspectSlide.exe`. Its icon appears in the notification area. Double-click the icon or choose **Settings** from its menu.
2. Choose a local photo folder. Ordinary subfolders are included. Set the interval in minutes (1 to 1440) and choose **Fill** or **Fit**. Fill/Fit is a Windows-wide wallpaper setting.
3. Use **Change now** for an immediate batch, **Pause** to stop scheduled changes, and **Restore previous** to return to the wallpapers that were present before AspectSlide's first change. Restore pauses the slideshow.
4. Enable **Start at sign-in** only if wanted. It creates one shortcut in your own Startup folder; disabling it removes that shortcut. Closing the settings window leaves the tray app running. **Exit** leaves the current wallpapers in place.

If Windows' own wallpaper slideshow is active, switch **Windows Settings → Personalization → Background** to **Picture**, then choose **Check again** in AspectSlide. Two slideshow schedulers cannot control the same background reliably.

**Windows colors** opens the system Colors page. Set the accent color to **Automatic** there if you want Windows to choose it from wallpaper. Windows does not document a way for AspectSlide to make the primary monitor's image the guaranteed accent source.

## Safety and limits

- Source photos are read only. Converted wallpapers and settings are stored under `%LOCALAPPDATA%\AspectSlide`.
- UNC paths, mapped network drives, folder links, junctions, file links, and files marked for cloud recall are skipped or rejected. Locally available OneDrive photos can be used.
- Wallpapers are prepared before any monitor changes. Windows applies them one monitor at a time, so the visible change is coordinated but not atomic.
- If the original desktop used a solid color, AspectSlide warns before its first change: that color cannot be restored automatically. Missing original image paths are likewise skipped on restore.
- The app does not use administrator rights, a service, telemetry, or network requests. It supports one Windows virtual desktop.
- The app accepts JPEG, PNG, BMP, GIF, and TIFF through Windows' built-in image handling. A selected image that needs rotation or conversion is cached as JPEG. Unsupported or corrupt files are skipped.

## Build and test

The source targets .NET 10 Windows Desktop and Windows 11. Install the official [.NET 10 SDK](https://learn.microsoft.com/en-us/dotnet/core/install/windows) to build:

```powershell
dotnet test AspectSlide.sln -c Release
dotnet publish src/AspectSlide/AspectSlide.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

The framework-dependent publish requires the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) on the target PC. For a portable build with the runtime bundled, publish with `--self-contained true` instead.

The default test suite uses temporary folders and fake wallpaper adapters; it does not alter the desktop. To run the opt-in, read-only COM smoke test on an interactive Windows 11 desktop:

```powershell
$env:ASPECTSLIDE_REAL_COM = '1'
dotnet test tests/AspectSlide.Tests/AspectSlide.Tests.csproj --filter FullyQualifiedName~WallpaperComSmokeTests
```
