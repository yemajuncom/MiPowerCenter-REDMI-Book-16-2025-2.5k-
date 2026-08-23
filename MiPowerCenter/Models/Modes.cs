using System.Collections.Generic;
using System.Windows.Media.Imaging;

namespace MiPowerCenter.Models;

public enum PerformanceMode
{
    Quiet = 0,
    Extreme = 2,
    Fierce = 3,
    Smart = 10,
    PowerSave = 11
}

public static class ModeInfo
{
    public static string GetName(PerformanceMode m) => m switch
    {
        PerformanceMode.Quiet => "静谧模式",
        PerformanceMode.Extreme => "极速模式",
        PerformanceMode.Fierce => "狂暴模式",
        PerformanceMode.Smart => "智能模式",
        PerformanceMode.PowerSave => "省电模式",
        _ => m.ToString()
    };

    public static string GetSubtitle(PerformanceMode m) => m switch
    {
        PerformanceMode.Quiet => "低功耗，低噪音",
        PerformanceMode.Extreme => "高性能释放",
        PerformanceMode.Fierce => "全性能输出",
        PerformanceMode.Smart => "智能调度",
        PerformanceMode.PowerSave => "延长续航",
        _ => ""
    };

    public static string GetIcon(PerformanceMode m) => m switch
    {
        PerformanceMode.Quiet => "img_quiet.png",
        PerformanceMode.Extreme => "img_extreme.png",
        PerformanceMode.Fierce => "img_fierce.png",
        PerformanceMode.Smart => "img_smart.png",
        PerformanceMode.PowerSave => "img_powersave.png",
        _ => "img_smart.png"
    };

    public static BitmapImage GetIconImage(PerformanceMode m)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri("pack://application:,,,/Assets/" + GetIcon(m));
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    /// On AC + standard charger: [Quiet, Smart, Fierce]; else [PowerSave, Smart, Extreme]
    public static IReadOnlyList<PerformanceMode> GetAvailableList(bool onAc, bool standardCharger)
    {
        if (onAc && standardCharger)
            return new[] { PerformanceMode.Quiet, PerformanceMode.Smart, PerformanceMode.Fierce };
        return new[] { PerformanceMode.PowerSave, PerformanceMode.Smart, PerformanceMode.Extreme };
    }

    public static PerformanceMode FromValue(int v) => v switch
    {
        0 => PerformanceMode.Quiet,
        2 => PerformanceMode.Extreme,
        3 => PerformanceMode.Fierce,
        10 => PerformanceMode.Smart,
        11 => PerformanceMode.PowerSave,
        _ => PerformanceMode.Smart
    };
}