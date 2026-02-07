using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace SysStatsTray;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}

internal class TrayApplicationContext : ApplicationContext
{
    private NotifyIcon? _notifyIcon;
    private Thread? _updaterThread;
    private volatile bool _running = true;
    private volatile int _updateIntervalMs = 2000;
    
    // Configuration
    private const int BarWidth = 48;
    private const int BarHeight = 64;
    private const int BarGap = 5;
    
    private static readonly Color ColorCpu = Color.FromArgb(250, 85, 220, 130);
    private static readonly Color ColorRam = Color.FromArgb(250, 70, 150, 250);
    private static readonly Color ColorBorder = Color.White;

    private PerformanceCounter? _cpuCounter;
    private PerformanceCounter? _ramCounter;

    public TrayApplicationContext()
    {
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
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", true);
            _ = _cpuCounter.NextValue(); // Warm up
        }
        catch { /* CPU counter may not be available */ }

        try
        {
            _ramCounter = new PerformanceCounter("Memory", "% Committed Bytes In Use", null, true);
            _ = _ramCounter.NextValue(); // Warm up
        }
        catch { /* RAM counter may not be available */ }
    }

    private (float cpu, float ram) GetStats()
    {
        float cpu = 0f;
        float ram = 0f;

        try
        {
            if (_cpuCounter != null)
                cpu = _cpuCounter.NextValue();
        }
        catch { }

        try
        {
            if (_ramCounter != null)
                ram = _ramCounter.NextValue();
        }
        catch { }

        return (cpu, ram);
    }

    private Bitmap CreateDualBarIcon(float cpuPercent, float ramPercent)
    {
        int size = BarHeight;
        var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);

            int margin = 2;
            int h = size - 2 * margin;
            int barWidth = (size - 2 * margin - BarGap) / 2;

            // Left bar (CPU)
            int x0Cpu = margin;
            int x1Cpu = margin + barWidth - 1;
            int fillCpu = Math.Max(0, Math.Min(h, (int)(h * cpuPercent / 100)));
            int yTopCpu = size - margin - fillCpu;
            
            g.FillRectangle(new SolidBrush(ColorCpu), x0Cpu, yTopCpu, barWidth, fillCpu);
            using (var pen = new Pen(ColorBorder, 2))
            {
                g.DrawRectangle(pen, x0Cpu, margin, barWidth, h);
            }

            // Right bar (RAM)
            int x0Ram = x1Cpu + 1 + BarGap;
            int x1Ram = x0Ram + barWidth - 1;
            int fillRam = Math.Max(0, Math.Min(h, (int)(h * ramPercent / 100)));
            int yTopRam = size - margin - fillRam;
            
            g.FillRectangle(new SolidBrush(ColorRam), x0Ram, yTopRam, barWidth, fillRam);
            using (var pen = new Pen(ColorBorder, 2))
            {
                g.DrawRectangle(pen, x0Ram, margin, barWidth, h);
            }
        }

        return bitmap;
    }

    private void CreateNotifyIcon()
    {
        var (cpu, ram) = GetStats();
        var icon = CreateDualBarIcon(cpu, ram);

        _notifyIcon = new NotifyIcon
        {
            Icon = Icon.FromHandle(icon.GetHicon()),
            Text = $"CPU: {cpu:F1}%  RAM: {ram:F1}%",
            Visible = true
        };

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Open Task Manager", null, OpenTaskManager);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(CreateUpdateIntervalMenu(1000, "Update: 1s"));
        contextMenu.Items.Add(CreateUpdateIntervalMenu(2000, "Update: 2s"));
        contextMenu.Items.Add(CreateUpdateIntervalMenu(3000, "Update: 3s"));
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Exit", null, Exit);

        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.DoubleClick += (s, e) => OpenTaskManager(null, null);
    }

    private ToolStripMenuItem CreateUpdateIntervalMenu(int intervalMs, string label)
    {
        var item = new ToolStripMenuItem(label)
        {
            CheckOnClick = true
        };
        item.Click += (s, e) =>
        {
            _updateIntervalMs = intervalMs;
            UpdateMenuCheckStates();
        };
        return item;
    }

    private void UpdateMenuCheckStates()
    {
        if (_notifyIcon?.ContextMenuStrip == null) return;

        var items = _notifyIcon.ContextMenuStrip.Items;
        foreach (var item in items)
        {
            if (item is ToolStripMenuItem menuItem && menuItem.Text != null && menuItem.Text.StartsWith("Update:"))
            {
                int interval = menuItem.Text switch
                {
                    "Update: 1s" => 1000,
                    "Update: 2s" => 2000,
                    "Update: 3s" => 3000,
                    _ => 2000
                };
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
                var (cpu, ram) = GetStats();
                if (_notifyIcon != null)
                {
                    var icon = CreateDualBarIcon(cpu, ram);
                    var newIcon = Icon.FromHandle(icon.GetHicon());
                    
                    _notifyIcon.Icon = newIcon;
                    _notifyIcon.Text = $"CPU: {cpu:F1}%  RAM: {ram:F1}%";
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
        _notifyIcon?.Dispose();
        _cpuCounter?.Dispose();
        _ramCounter?.Dispose();
        ExitThread();
    }
}
