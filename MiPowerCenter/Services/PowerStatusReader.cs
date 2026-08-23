using System.Runtime.InteropServices;

namespace MiPowerCenter.Services;

public struct SystemPowerStatus
{
    public byte ACLineStatus;        // 0 offline, 1 online, 255 unknown
    public byte BatteryFlag;         // 1 high,2 low,4 critical,8 charging,128 no battery
    public byte BatteryLifePercent;  // 0-100, 255 unknown
    public byte Reserved1;
    public uint BatteryLifeTime;     // seconds, -1 unknown
    public uint BatteryFullLifeTime; // seconds, -1 unknown
}

public static class PowerStatusReader
{
    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(ref SystemPowerStatus lpSystemPowerStatus);

    public static SystemPowerStatus Read() => Read(out _);

    public static SystemPowerStatus Read(out bool ok)
    {
        SystemPowerStatus s = default;
        ok = GetSystemPowerStatus(ref s);
        return s;
    }
}