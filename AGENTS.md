# AGENTS.md

This file provides guidance to Codex (Codex.ai/code) when working with code in this repository.

## Build & Run

```powershell
dotnet build
dotnet run
dotnet publish -c Release -r win-x64 --self-contained
```

No test framework configured. No linter configured.

## Architecture

WPF desktop app (.NET 8, Windows-only) that replaces the Print Screen key with a region-select screenshot tool. Runs as a system tray app with `ShutdownMode.OnExplicitShutdown` — no main window visible.

**Feature folders**: Code is organized by feature, each with its own namespace root:
- `Screenshot/` (`ScreenShotManager.Screenshot.*`) — region capture, overlay, settings.
- `VideoDownscaler/` (`ScreenShotManager.VideoDownscaler.*`) — Explorer "Downscale Video" flow.
- `VolumeManager/` (`ScreenShotManager.VolumeManager.*`) — microphone volume forcing.
- `Shared/` (`ScreenShotManager.Shared.*`) — cross-feature `Helpers/`, `Converters/`, `Components/`, and the tray (`SystemTrayService`).
- Root (`ScreenShotManager`) — `App` (incl. partial `VideoDownscaler/App.Downscale.cs`), `MainWindow`. Each feature keeps `Services/` + `Services/Interfaces/`.

**Lifecycle**: `App.xaml.cs` is the entry point. It creates all services manually (no DI container — except the downscale feature, wired via `Microsoft.Extensions.DependencyInjection` in `App.Downscale.cs`), wires events, and coordinates the capture flow: PrintScreen pressed → fullscreen frozen → OverlayWindow shown as modal dialog → user selects region + optional annotation → bitmap saved to file + copied to clipboard.

**Key services** (each feature's interfaces live in its own `Services/Interfaces/`):
- `KeyboardHookService` (`Screenshot/Services/`) — Win32 low-level keyboard hook (`SetWindowsHookEx`) to intercept PrtSc globally. Swallows the key (returns 1) when active.
- `ScreenCaptureService` (`Screenshot/Services/`) — GDI+ `CopyFromScreen` for capture, `BitmapHelper` for WinForms↔WPF bitmap conversion.
- `SystemTrayService` (`Shared/Services/`) — WinForms `NotifyIcon` for tray icon and context menu (WPF doesn't have a native tray API).
- `SettingsService` (`Screenshot/Services/`) — JSON config at `%AppData%\ScreenShotManager\config.json`. Also manages Windows startup registry key (`HKCU\...\Run`).
- `InputVolumeMonitorService` (`VolumeManager/Services/`) — NAudio-based background service polling default microphone volume every 15s, forcing it to 100%. Unrelated to screenshot functionality.

**Overlay flow** (`Screenshot/Views/Overlay/OverlayWindow`): Spans all monitors via `SystemParameters.VirtualScreen*`. Uses four dim rectangles around the selection (not a clipping path). Annotation draws `Polyline` strokes on a separate canvas, composited via `RenderTargetBitmap` + `VisualBrush` at save time.

**Settings window** (`Screenshot/Views/Settings/SettingsWindow`): Auto-saves on every change. "Start on login" triggers UAC elevation by relaunching the process with `runas` verb and `/settings` arg.

## Conventions

- `ImplicitUsings` disabled — all `using` statements explicit.
- Both `UseWPF` and `UseWindowsForms` enabled (WinForms needed for `NotifyIcon`, `ColorDialog`, `FolderBrowserDialog`).
- Custom WPF components (`CustomDropdown`, `CustomTitleBar`) in `Shared/Components/`.
- `BitmapHelper` has two conversion paths: `BitmapToBitmapSource` (HBitmap/GDI) and `BitmapToBitmapSourceOptimized` (LockBits, used for large captures). Both freeze the result for cross-thread use.
