# SysStatsTray

A system tray icon that monitors CPU and RAM usage in real time.

## Requirements

- .NET 8.0 SDK (or later)
- Windows (for system tray support)

## Build

```bash
dotnet build
dotnet build -c Debug
dotnet build -c Release
```

## Run

```bash
dotnet run
```

Or run the compiled executable directly:
```bash
SysStatsTray.exe
```

## Features

- **Tooltip**: Hover over the tray icon to see current CPU % and RAM %.
- **Icon**: Left bar = CPU usage (green), right bar = RAM usage (blue).
- **Left-click/Double-click**: Opens Windows Task Manager.
- **Right-click menu**: 
  - Open Task Manager
  - Update interval: Choose 1s, 2s, or 3s (default: 2s)
  - Exit

Stats refresh every 2 seconds by default.

## Notes

- The application runs as a background service in the Windows system tray.
- Requires administrative privileges for accurate CPU measurements on some Windows versions.
- Built with .NET 8.0 and Windows Forms.
