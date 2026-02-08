using System.Drawing;
using System.Text.Json;

namespace SysStatsTray;

internal class AppConfig
{
    public bool DebugPrint { get; set; } = false;
    public int UpdateIntervalMs { get; set; } = 1000;
    public int[] UpdateIntervalOptions { get; set; } = [1000, 2000, 3000];
    public int BarHeight { get; set; } = 32;
    public int BarGap { get; set; } = 3;
    public string ColorCpu { get; set; } = "#FA55DC82";
    public string ColorRam { get; set; } = "#FA4696FA";
    public string ColorBorder { get; set; } = "#FFFFFFFF";
    public int BarUpdateThreshold { get; set; } = 3;

    public static AppConfig Load()
    {
        var baseDir = AppContext.BaseDirectory;
        var path = Path.Combine(baseDir, "config.json");
        if (!File.Exists(path))
            return new AppConfig();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
        }
        catch
        {
            return new AppConfig();
        }
    }

    public Color ParseColor(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return Color.White;
        hex = hex.TrimStart('#');
        if (hex.Length == 6)
            hex = "FF" + hex;
        if (hex.Length != 8) return Color.White;
        try
        {
            var a = Convert.ToByte(hex.Substring(0, 2), 16);
            var r = Convert.ToByte(hex.Substring(2, 2), 16);
            var g = Convert.ToByte(hex.Substring(4, 2), 16);
            var b = Convert.ToByte(hex.Substring(6, 2), 16);
            return Color.FromArgb(a, r, g, b);
        }
        catch { return Color.White; }
    }
}
