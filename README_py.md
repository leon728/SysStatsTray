# SysStatsTray

A system tray icon that monitors CPU and RAM usage in real time.

## Setup

```bash
pip install -r requirements.txt
```

## Run

```bash
pythonw systray_monitor.py
```

- **Tooltip**: Hover over the tray icon to see current CPU % and RAM %.
- **Icon**: Top bar = CPU usage (blue), bottom bar = RAM usage (green).
- **Menu**: Right-click the icon and choose **Exit** to quit.

Stats refresh every 2 seconds.
