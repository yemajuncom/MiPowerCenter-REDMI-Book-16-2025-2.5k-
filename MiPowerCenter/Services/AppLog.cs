using System.IO;

namespace MiPowerCenter.Services;

public static class AppLog
{
    private static readonly object _lock = new();
    private static readonly string _path = Path.Combine(Path.GetTempPath(), "MiPowerCenter.log");

    public static void Write(string msg)
    {
        try
        {
            lock (_lock)
            {
                File.AppendAllText(_path, DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + Environment.NewLine);
            }
        }
        catch { }
    }
}