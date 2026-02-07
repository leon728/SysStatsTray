"""
System tray icon: one icon with two vertical bars (CPU left, RAM right).
"""
import subprocess
import threading
import time
from pathlib import Path

from PIL import Image, ImageDraw

import psutil
import pystray


def _log_startup_error(exc: Exception) -> None:
    """Log startup errors to a file (useful when run via pythonw where stderr is hidden)."""
    log_path = Path(__file__).resolve().parent / "systray_startup.log"
    try:
        with open(log_path, "a", encoding="utf-8") as f:
            import traceback
            from datetime import datetime
            f.write(f"\n--- {datetime.now()} ---\n")
            traceback.print_exc(file=f)
    except Exception:
        pass

# --- Global config ---
BAR_WIDTH = 48
BAR_HEIGHT = 64
BAR_GAP = 5
UPDATE_INTERVAL_SEC = 2
COLOR_CPU = (85, 220, 130, 250)
COLOR_RAM = (70, 150, 250, 250)


def get_stats():
    """Return current CPU and RAM usage as (cpu_percent, ram_percent)."""
    cpu = psutil.cpu_percent(interval=None)
    mem = psutil.virtual_memory()
    return cpu, mem.percent


def create_dual_bar_icon(cpu_percent: float, ram_percent: float) -> Image.Image:
    """Draw one icon with two vertical bars: CPU left, RAM right.
    Fills from bottom to top. Square canvas for Windows tray.
    """
    size = BAR_HEIGHT
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    margin = 2
    h = size - 2 * margin
    bar_width = (size - 2 * margin - BAR_GAP) // 2

    # Left bar (CPU)
    x0_cpu = margin
    x1_cpu = margin + bar_width - 1
    fill_cpu = max(0, min(h, int(h * cpu_percent / 100)))
    y_top_cpu = size - margin - fill_cpu
    d.rectangle([x0_cpu, y_top_cpu, x1_cpu, size - margin], fill=COLOR_CPU)
    d.rectangle([x0_cpu, margin, x1_cpu, size - margin], outline=(255, 255, 255, 255), width=2)

    # Right bar (RAM) — BAR_GAP pixels after left bar
    x0_ram = x1_cpu + 1 + BAR_GAP
    x1_ram = x0_ram + bar_width - 1
    fill_ram = max(0, min(h, int(h * ram_percent / 100)))
    y_top_ram = size - margin - fill_ram
    d.rectangle([x0_ram, y_top_ram, x1_ram, size - margin], fill=COLOR_RAM)
    d.rectangle([x0_ram, margin, x1_ram, size - margin], outline=(255, 255, 255, 255), width=2)

    return img


# Shared state for the updater thread
_icon = None
_running = True
_update_interval_sec = UPDATE_INTERVAL_SEC


def set_interval(sec: int):
    global _update_interval_sec
    _update_interval_sec = sec


def run_updater():
    """Update the tray icon in a loop."""
    global _icon, _running, _update_interval_sec
    while _running:
        try:
            cpu, ram = get_stats()
            if _icon:
                _icon.title = f"CPU: {cpu:.1f}%  RAM: {ram:.1f}%"
                _icon.icon = create_dual_bar_icon(cpu, ram)
        except Exception:
            pass
        interval = _update_interval_sec
        time.sleep(interval)


def _effective_interval():
    """Current effective update interval in seconds (for menu check state)."""
    return _update_interval_sec


def open_task_manager(*_):
    """Launch Windows Task Manager (left-click / double-click action)."""
    try:
        subprocess.Popen("taskmgr", shell=True)
    except Exception:
        pass


def run_icon():
    global _icon
    cpu, ram = get_stats()
    menu = pystray.Menu(
        pystray.MenuItem("Open Task Manager", open_task_manager, default=True),
        pystray.Menu.SEPARATOR,
        pystray.MenuItem("Update: 1s", lambda i: set_interval(1), checked=lambda _: _effective_interval() == 1),
        pystray.MenuItem("Update: 2s", lambda i: set_interval(2), checked=lambda _: _effective_interval() == 2),
        pystray.MenuItem("Update: 3s", lambda i: set_interval(3), checked=lambda _: _effective_interval() == 3),
        pystray.MenuItem("Exit", lambda i: stop_all()),
    )
    _icon = pystray.Icon(
        "SysStatsTray",
        create_dual_bar_icon(cpu, ram),
        title=f"CPU: {cpu:.1f}%  RAM: {ram:.1f}%",
        menu=menu,
        on_activate=open_task_manager,
    )
    _icon.run()


def stop_all():
    global _running, _icon
    _running = False
    if _icon:
        _icon.stop()


def main():
    global _running
    updater = threading.Thread(target=run_updater, daemon=True)
    updater.start()

    icon_thread = threading.Thread(target=run_icon, daemon=False)
    icon_thread.start()
    icon_thread.join()


if __name__ == "__main__":
    try:
        main()
    except Exception as e:
        _log_startup_error(e)
        raise
