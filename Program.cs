using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace SysStatsTray;

[StructLayout(LayoutKind.Sequential)]
internal struct MEMORYSTATUSEX
{
    public uint dwLength;
    public uint dwMemoryLoad;
    public ulong ullTotalPhys;
    public ulong ullAvailPhys;
    public ulong ullTotalPageFile;
    public ulong ullAvailPageFile;
    public ulong ullTotalVirtual;
    public ulong ullAvailVirtual;
    public ulong ullAvailExtendedVirtual;
}

internal static class NativeMethods
{
    internal const int ATTACH_PARENT_PROCESS = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AttachConsole(int dwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr hIcon);
}

internal static class Program
{
    [STAThread]
    static void Main()
    {
        // WinExe has no console; attach to parent (e.g. terminal from "dotnet run") so Console.Out is visible
        if (NativeMethods.AttachConsole(NativeMethods.ATTACH_PARENT_PROCESS))
        {
            try
            {
                Console.SetOut(new System.IO.StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            }
            catch { /* ignore if stdout not available */ }
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}

internal class TrayApplicationContext : ApplicationContext
{
    private readonly AppConfig _config = AppConfig.Load();
    private NotifyIcon? _notifyIcon;
    private Thread? _updaterThread;
    private volatile bool _running = true;
    private volatile int _updateIntervalMs;

    private int BarHeight;
    private int BarGap;
    private Color ColorCpu;
    private Color ColorRam;
    private Color ColorBorder;

    private static float _lastCpuPercent = 999;
    private static float _lastRamPercent = 999;

    private static string GetUptimeString()
    {
        long totalMs = Math.Max(0, Environment.TickCount64);
        int days = (int)(totalMs / (24 * 60 * 60 * 1000));
        totalMs %= (24 * 60 * 60 * 1000);
        int hours = (int)(totalMs / (60 * 60 * 1000));
        totalMs %= (60 * 60 * 1000);
        int minutes = (int)(totalMs / (60 * 1000));
        // return $"{days} D {hours} H {minutes} M";
        return $"{days} days, {hours}:{minutes}";
    }

    private PerformanceCounter? _cpuCounter;

    /// <summary>HICON we gave to the current tray icon. Icon.FromHandle does not own it; we must DestroyIcon when replacing.</summary>
    private IntPtr _currentIconHandle = IntPtr.Zero;
    private readonly object _iconLock = new();
    /// <summary>Reused bitmap for tray icon updates to avoid allocating a new Bitmap every tick.</summary>
    private Bitmap? _reusedIconBitmap;

    public TrayApplicationContext()
    {
        _updateIntervalMs = _config.UpdateIntervalMs;
        BarHeight = _config.BarHeight;
        BarGap = _config.BarGap;
        ColorCpu = _config.ParseColor(_config.ColorCpu);
        ColorRam = _config.ParseColor(_config.ColorRam);
        ColorBorder = _config.ParseColor(_config.ColorBorder);
        try
        {
            InitializePerformanceCounters();
            CreateNotifyIcon();
            StartUpdaterThread();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error initializing: {ex.Message}", "SysStatsTray Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            ExitThread();
        }
    }

    private void InitializePerformanceCounters()
    {
        try
        {
            // "% Processor Time" (0-100% for _Total); matches the 0-100% scale users expect and Task Manager's classic view.
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", true);

            // // "% Processor Utility" (Task Manager on Win8+) can exceed 100% with Turbo Boost
            // // You can clamp it to 0-100% by using: cpu = Math.Clamp(_cpuCounter.NextValue(), 0f, 100f);
            // _cpuCounter = new PerformanceCounter("Processor Information", "% Processor Utility", "_Total", true);

            _ = _cpuCounter.NextValue(); // Warm up
        }
        catch { /* CPU counter may not be available */ }
    }

    private (float cpu, float ramPercent, float usedGB, float totalGB) GetStats()
    {
        float cpu = 0f;
        float ramPercent = 0f;
        float usedGB = 0f;
        float totalGB = 0f;

        try
        {
            if (_cpuCounter != null)
                cpu = _cpuCounter.NextValue();
        }
        catch { }

        // Use physical memory % (matches Task Manager "In use"), not commit charge.
        try
        {
            var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (NativeMethods.GlobalMemoryStatusEx(ref mem))
            {
                totalGB = (float)(mem.ullTotalPhys / (1024.0 * 1024.0 * 1024.0));
                usedGB = (float)((mem.ullTotalPhys - mem.ullAvailPhys) / (1024.0 * 1024.0 * 1024.0));
                // ramPercent = mem.dwMemoryLoad;
                ramPercent = usedGB / totalGB * 100;
            }
        }
        catch { }

        return (cpu, ramPercent, usedGB, totalGB);
    }

    /// <summary>Draws the dual CPU/RAM bar into an existing bitmap (size BarHeight x BarHeight). Reused to avoid allocations.</summary>
    private void DrawDualBarToBitmap(Bitmap bitmap, float cpuPercent, float ramPercent)
    {
        // Skip drawing if the values are too similar to the last draw
        if (   Math.Abs(cpuPercent - _lastCpuPercent) < _config.BarUpdateThreshold
            && Math.Abs(ramPercent - _lastRamPercent) < _config.BarUpdateThreshold)
        {
            return;
        }

        _lastCpuPercent = cpuPercent;
        _lastRamPercent = ramPercent;

        int size = BarHeight;
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);

            int margin = 1;
            int h = size - 2 * margin;
            int barWidth = (size - 2 * margin - BarGap) / 2;

            // Left bar (CPU)
            int x0Cpu = margin;
            int x1Cpu = margin + barWidth - 1;
            int fillCpu = Math.Max(0, Math.Min(h, (int)(h * cpuPercent / 100)));
            int yTopCpu = size - margin - fillCpu;

            using (var brushCpu = new SolidBrush(ColorCpu))
                g.FillRectangle(brushCpu, x0Cpu, yTopCpu, barWidth, fillCpu);
            using (var pen = new Pen(ColorBorder, 1))
                g.DrawRectangle(pen, x0Cpu, margin, barWidth, h);

            // Right bar (RAM)
            int x0Ram = x1Cpu + 1 + BarGap;
            int fillRam = Math.Max(0, Math.Min(h, (int)(h * ramPercent / 100)));
            int yTopRam = size - margin - fillRam;

            using (var brushRam = new SolidBrush(ColorRam))
                g.FillRectangle(brushRam, x0Ram, yTopRam, barWidth, fillRam);
            using (var pen = new Pen(ColorBorder, 1))
                g.DrawRectangle(pen, x0Ram, margin, barWidth, h);
        }
    }

    private void CreateNotifyIcon()
    {
        _reusedIconBitmap = new Bitmap(BarHeight, BarHeight);
        _notifyIcon = new NotifyIcon
        {
            Visible = true
        };

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Open Task Manager", null, OpenTaskManager);
        contextMenu.Items.Add(new ToolStripSeparator());
        foreach (int intervalMs in _config.UpdateIntervalOptions)
        {
            string label = intervalMs >= 1000 ? $"Update: {intervalMs / 1000}s" : $"Update: {intervalMs}ms";
            contextMenu.Items.Add(CreateUpdateIntervalMenu(intervalMs, label));
        }
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Exit", null, Exit);

        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.DoubleClick += (s, e) => OpenTaskManager(null, null);
        UpdateMenuCheckStates();
    }

    private ToolStripMenuItem CreateUpdateIntervalMenu(int intervalMs, string label)
    {
        var item = new ToolStripMenuItem(label)
        {
            CheckOnClick = true,
            Tag = intervalMs
        };
        item.Click += (s, e) =>
        {
            _updateIntervalMs = intervalMs;
            UpdateMenuCheckStates();
            // Reclaim memory
            GC.Collect();
        };
        return item;
    }

    private void UpdateMenuCheckStates()
    {
        if (_notifyIcon?.ContextMenuStrip == null) return;

        var items = _notifyIcon.ContextMenuStrip.Items;
        foreach (var item in items)
        {
            if (item is ToolStripMenuItem menuItem && menuItem.Tag is int interval)
            {
                menuItem.Checked = (interval == _updateIntervalMs);
            }
        }
    }

    private void StartUpdaterThread()
    {
        _updaterThread = new Thread(UpdaterThreadWork)
        {
            IsBackground = true,
            Name = "SysStatsTray Updater"
        };
        _updaterThread.Start();
    }

    private void UpdaterThreadWork()
    {
        while (_running)
        {
            try
            {
                var (cpu, ramPercent, usedGB, totalGB) = GetStats();
                if (_notifyIcon != null && _reusedIconBitmap != null)
                {
                    DrawDualBarToBitmap(_reusedIconBitmap, cpu, ramPercent);
                    IntPtr hIcon = _reusedIconBitmap.GetHicon();
                    lock (_iconLock)
                    {
                        if (_currentIconHandle != IntPtr.Zero)
                            NativeMethods.DestroyIcon(_currentIconHandle);
                        _currentIconHandle = hIcon;
                        _notifyIcon.Icon = Icon.FromHandle(_currentIconHandle);
                        // _notifyIcon.Text = $"CPU: {cpu:F1}%\r\nRAM: {ramPercent:F1}% ({usedGB:F1}/{totalGB:F1} GB)\r\nUptime: {GetUptimeString()}";
                        _notifyIcon.Text = $"CPU: {cpu:F0}%\r\nRAM: {ramPercent:F0}% ({usedGB:F1}/{totalGB:F1} GB)"; // F0 format for integers, F1 format for .1f
                    }
                }
                if (_config.DebugPrint)
                {
                    Console.Out.WriteLine($"CPU: {cpu,4:F1}% | RAM: {ramPercent,4:F1}% ({usedGB:F1}/{totalGB:F1} GB)");
                }
            }
            catch { }

            Thread.Sleep(_updateIntervalMs);
        }
    }

    private void OpenTaskManager(object? sender, EventArgs? e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "taskmgr.exe",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open Task Manager: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Exit(object? sender, EventArgs? e)
    {
        _running = false;
        lock (_iconLock)
        {
            if (_currentIconHandle != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(_currentIconHandle);
                _currentIconHandle = IntPtr.Zero;
            }
        }
        _reusedIconBitmap?.Dispose();
        _reusedIconBitmap = null;
        _notifyIcon?.Dispose();
        _cpuCounter?.Dispose();
        ExitThread();
    }
}
