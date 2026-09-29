# Monitor-aware wallpaper slideshow for Windows 11

Status: In-chat design approved; written spec awaiting review.

## Purpose

Provide a changing desktop wallpaper on a mixed-orientation monitor setup. At each configured interval, every connected monitor receives a different photo chosen from one folder and its subfolders. Selection favors the monitor's orientation and aspect ratio, so portrait photos normally go to vertical screens and landscape photos to horizontal screens.

The design follows four first principles: minimal features, low idle resource use, predictable behavior, and safe handling of the user's files and Windows settings.

## Product behavior

- Target Windows 11 and one Windows virtual desktop. Any number of connected monitors may be used.
- The user selects one local folder. The app includes images in all ordinary subfolders. Directory links and junctions are not traversed, preventing cycles and unintended access outside the chosen tree.
- A single configurable timer changes all connected monitors together. The default interval is 30 minutes; the user may set any interval from 1 minute to 24 hours. Change now works even while paused and does not resume the timer.
- All monitors display different files in a change batch whenever the catalog contains at least that many valid files. Reuse is allowed only when there are too few unique files.
- The app prefers an image with the same orientation as the monitor. Within that group, it favors the closest aspect ratio and avoids needless upscaling. When no same-orientation image is available, it uses the closest available image.
- The default display mode is Fill. The user may choose Fit instead. This is one setting shared by all monitors because the documented Windows wallpaper position API is system-wide.
- Start at sign-in is off by default. If enabled, the app creates a removable shortcut in the current user's Startup folder. Closing the settings window leaves the tray app running; Exit stops future changes and leaves the current wallpaper visible.
- Windows can derive an accent color from the background when the user selects Automatic in Windows Settings > Personalization > Colors. The app provides a link to that setting. It does not promise that Windows samples the primary monitor because Microsoft does not document the source monitor.

## User interface

One compact settings window, patterned after the relevant Windows Background controls:

1. Folder picker and a count of valid images found, including subfolders.
2. Monitor preview showing each monitor's current image, orientation, and resolution.
3. Change interval, Fill/Fit choice, and Start at sign-in toggle.
4. Change now, Pause/Resume, Restore previous wallpapers, Open Windows accent-color settings, and Exit.
5. A small status area for a missing folder, unreadable images, or a competing Windows slideshow.

The tray menu offers Open settings, Change now, Pause/Resume, and Exit. No gallery management, cloud sync, accounts, telemetry, or online image sources are needed.

## Architecture

Use one per-user, memory-managed C# desktop process with a small tray icon and settings window. A Windows Forms UI keeps the first version compact; the window is created only when opened. The process does not run as a service and does not require administrator rights.

The process has five focused parts:

- **Catalog:** recursively indexes image paths and reads displayed dimensions, including EXIF rotation, through Windows Imaging Component codecs installed on the machine. It caches path, size, modification time, and dimensions in local app data. It never writes to source images. If a selected source format cannot be used directly as wallpaper, the app creates a normalized JPEG in its own cache for that image only.
- **Monitor adapter:** obtains connected monitor IDs and rectangles using `IDesktopWallpaper`, then sets one wallpaper path per monitor.
- **Selector:** forms one batch of distinct images, preferring orientation, aspect ratio, resolution, and images not recently displayed. It tracks shuffle history so immediate repeats are avoided when alternatives exist.
- **Scheduler:** owns one timer and handles Change now, pause, sign-in startup, wake from sleep, and monitor changes. If a deadline passes during sleep, it performs at most one catch-up change.
- **Settings/state:** stores the chosen folder, interval, fit mode, startup preference, pause state, catalog metadata, and shuffle history under the current user's app data.

The selector compares aspect ratios using the absolute log ratio between image and monitor. It adds a penalty when an image would need upscaling. Orientation mismatch is considered only when the matching-orientation pool is empty or distinct assignment otherwise cannot fill the connected monitors. Randomness is applied among suitable candidates, with recent images penalized, so the result remains a slideshow rather than a fixed list of best matches. Selection evaluates the whole monitor batch so the same file is not assigned twice when avoidable.

## Efficiency and safety

- The idle process waits on a timer and operating-system notifications; it does not continuously poll displays or re-scan the folder.
- Initial indexing and later updates read image metadata without decoding full-resolution pixels. A selected image is decoded only if it needs a normalized cache file; preview thumbnails are generated only while settings is open. File change notifications are debounced; a reconciliation scan at startup and once per day repairs missed events.
- The app reads the photo tree but never edits, deletes, renames, or moves its files. Its own cache and settings remain in per-user app data.
- No network access, telemetry, elevated privileges, Windows service, or unsupported accent-color registry changes are part of the design.
- On first activation, the app checks whether the native Windows slideshow is active. If it is, the settings window explains the conflict and links to Windows Background settings. The user can select Check again after changing Windows to Picture. The app waits until native slideshow is off before controlling wallpaper, avoiding two schedulers competing.
- If the folder disappears, an image fails to decode, or a wallpaper API call fails, the app keeps the last successful wallpaper, records a concise error, and retries on the next relevant event. One bad file does not stop the slideshow.
- Before its first controlled change, the app saves the current wallpaper path for each attached monitor and the wallpaper position. Restore previous wallpapers reapplies that saved state only when the user selects the action and the saved files are still accessible. If any file is missing, it reports that fact without replacing the current wallpaper on that monitor. Exit never restores automatically, which could overwrite a later user choice.

## Acceptance checks

1. With one landscape and one portrait monitor, each receives a distinct image of matching orientation when both types exist; both change on the same interval.
2. With two monitors of the same orientation, each receives a different image when the folder contains enough images.
3. An ultrawide monitor tends to receive closer-ratio landscape images than an ordinary landscape monitor while shuffle continues to vary the choices.
4. With no matching-orientation image, the monitor receives the closest available image.
5. Fill crops as needed; Fit shows the full image with borders. Switching modes affects all monitors.
6. New and removed photos in subfolders update the catalog without a full scan on every interval.
7. Sleep/wake, monitor rotation, monitor disconnect/reconnect, unreadable files, and a temporarily missing folder do not cause rapid repeated changes or modification of source photos.
8. The app starts at sign-in only if enabled and can be paused or exited from the tray. After Exit, current wallpapers remain visible.
9. The app can open Windows accent-color settings; with Automatic selected there, Windows owns the accent-color choice. A specific source monitor is not guaranteed.
10. The app never changes the contents of the chosen photo folder, never contacts a network endpoint, and never needs administrator privileges.

## Windows API references

- [Set wallpaper per monitor](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-idesktopwallpaper-setwallpaper)
- [Get monitor rectangle](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-idesktopwallpaper-getmonitorrect)
- [Set wallpaper position](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-idesktopwallpaper-setposition)
- [Get native slideshow status](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-idesktopwallpaper-getstatus)
- [Windows automatic accent color](https://support.microsoft.com/en-us/windows/experience/personalization/personalize-your-colors-in-windows)
