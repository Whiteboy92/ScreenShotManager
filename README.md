# 📸 ScreenShotManager

A powerful, lightweight desktop screenshot tool for Windows built with WPF (.NET 8) that replaces the default Print Screen functionality with advanced capture and annotation features.

![Platform](https://img.shields.io/badge/platform-Windows-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-purple)
![License](https://img.shields.io/badge/license-MIT-green)

## ✨ Features

### 🎯 Smart Screen Capture
- **Global Hotkey**: Press `PrtSc` to instantly activate the capture overlay
- **Multi-Monitor Support**: Seamlessly capture across all connected displays
- **Frozen Screen Preview**: The screen freezes when you press PrtSc, allowing you to select any area without distractions
- **Flexible Selection**: Click and drag to select any rectangular area
- **Resizable Selection**: Adjust your selection with corner handles before capturing
- **Dimming Effect**: Non-selected areas are dimmed for better focus (can be disabled in settings)

### 🎨 Annotation Tools
After selecting a region, annotate your screenshot before saving:
- **Drawing Mode**: Toggle drawing with the brush icon or press `D`
- **Color Palette**: Quick access to 6 preset colors (Red, Green, Blue, Yellow, Black, White)
- **Custom Color Picker**: Full RGB color selection with a color dialog
- **Adjustable Brush Size**: 1-50px with slider and +/- buttons
- **Clear Drawings**: Remove all annotations with one click
- **Real-time Preview**: See your annotations before saving

### 💾 Auto-Save & Clipboard
- **Instant Clipboard Copy**: Automatically copies to clipboard for quick pasting
- **Auto-Save**: Screenshots are automatically saved to your chosen folder
- **Multiple Formats**: Support for PNG (recommended), JPEG, and BMP
- **Custom Naming**: Configurable filename patterns with date/time tags
- **History Management**: Optional auto-cleanup of old screenshots

### ⚙️ Settings & Customization
- **Save Location**: Choose where screenshots are saved
- **File Format Dropdown**: Modern UI with format descriptions
  - PNG: Lossless compression, best quality
  - JPEG: Smaller file size, lossy compression
  - BMP: Uncompressed, largest file size
- **Filename Pattern**: Customize with `{date}` and `{time}` tags
- **Dimming Toggle**: Enable/disable the screen dimming effect
- **Startup Option**: Run automatically when Windows starts (requires admin)
- **Auto-Clean**: Set maximum number of screenshots to keep
- **Auto-Save**: All settings changes are saved instantly

### 🖥️ System Integration
- **System Tray**: Runs quietly in the background
- **Quick Access Menu**:
  - 📸 Take Screenshot
  - ⚙️ Open Settings
  - 📁 Open Save Folder
  - ⏸️ Pause Hotkey
  - ❌ Exit
- **Tray Notifications**: Get notified when screenshots are captured

## 🎮 Keyboard Shortcuts

| Key | Action |
|-----|--------|
| `PrtSc` | Activate screenshot overlay |
| `Click + Drag` | Select area |
| `Enter` | Capture selected area |
| `Ctrl + C` | Capture and copy to clipboard |
| `Esc` | Cancel capture |
| `D` | Toggle drawing mode |

## 🚀 Getting Started

### Requirements
- Windows 10/11
- .NET 8.0 Runtime

### Installation
1. Download the latest release
2. Extract to your preferred location
3. Run `ScreenShotManager.exe`
4. The app will start in the system tray

### First-Time Setup
1. **Right-click** the tray icon
2. Select **"Open Settings"**
3. Configure your preferences:
   - Choose a save folder
   - Select your preferred file format
   - Customize the filename pattern
   - Enable "Start on Windows login" if desired

## 📁 Default Settings

- **Save Folder**: `%UserProfile%\Pictures\Screenshots`
- **File Format**: PNG
- **Filename Pattern**: `Screenshot_{date}_{time}`
- **Max History**: 100 screenshots
- **Config Location**: `%AppData%\ScreenShotManager\config.json`

## 🎨 User Interface

### Modern Dark Theme
- Clean, minimalist design
- Smooth animations and transitions
- Custom-styled dropdown with format descriptions
- Invisible scrollbar with gentle scrolling speed
- Custom title bar with window controls

### Annotation Toolbar
- Floating toolbar near selection
- Auto-repositions if near screen edges
- Intuitive icon-based controls
- Real-time size preview
- Visual feedback for active tools

## 🛠️ Technical Details

### Built With
- **Framework**: WPF (.NET 8)
- **Architecture**: SOLID principles with clean separation of concerns
- **Services**: Dependency injection pattern
- **Keyboard Hook**: Global Windows keyboard hook (Win32 API)
- **Screen Capture**: GDI+ for high-quality captures
- **Settings**: JSON-based configuration

### Project Structure
```
ScreenShotManager/
├── Views/
│   ├── Overlay/          # Screen capture overlay
│   ├── Settings/         # Settings window
│   └── Components/       # Reusable UI components
├── Services/             # Business logic
│   ├── KeyboardHookService
│   ├── ScreenCaptureService
│   ├── SettingsService
│   └── SystemTrayService
├── Models/               # Data models
├── Helpers/              # Utility classes
└── Converters/           # XAML value converters
```

## 🔧 Advanced Configuration

Edit `%AppData%\ScreenShotManager\config.json` for advanced options:

```json
{
  "SaveFolder": "C:\\Screenshots",
  "FileFormat": "PNG",
  "FilenamePattern": "Screenshot_{date}_{time}",
  "DisableDimming": false,
  "StartOnLogin": false,
  "MaxAutoSaveHistory": 100
}
```

## 🐛 Troubleshooting

**Screenshot doesn't work:**
- Ensure the app is running (check system tray)
- Try unpausing the hotkey (right-click tray icon)
- Restart the application

**Can't enable "Start on Windows login":**
- This feature requires administrator privileges
- The app will prompt for UAC when you check the option
- Approve the UAC dialog to enable the feature

**Hotkey conflicts with other apps:**
- Use the "Pause Hotkey" option to temporarily disable
- Close other screenshot tools that may intercept PrtSc

## 📝 License

This project is licensed under the MIT License - feel free to use, modify, and distribute.
