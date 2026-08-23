using System;
using System.IO;
using System.Reflection;

class ChargingProbe8
{
    const string XiaomiDir = @"C:\Program Files\MI\XiaomiPCManager\5.8.1.121";
    const string LogPath = @"C:\Program Files\MI\mitool\chargingprobe\log8.txt";
    static readonly object Sync = new object();
    static void Log(string s) { lock (Sync) File.AppendAllText(LogPath, s + "\r\n"); }
    static Type T;
    static object C;
    static volatile int LastPct = -1;
    static volatile int FlatCount = 0;
    public static int GetLast() => LastPct;
    public static void SetLast(int p) { LastPct = p; FlatCount = 0; }
    public static void IncFlat() => FlatCount++;
    static readonly object PctLock = new object();

    static void Main()
    {
        File.Delete(LogPath);
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            string name = new AssemblyName(e.Name).Name + ".dll";
            string p = Path.Combine(XiaomiDir, name);
            return File.Exists(p) ? Assembly.LoadFrom(p) : null;
        };
        var asm = Assembly.LoadFrom(Path.Combine(XiaomiDir, "SvrCModuleClrWrapper.dll"));
        T = asm.GetType("SvrCModuleClrWrapper.ModuleController");
        C = T.GetMethod("GetInstance", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
        T.GetEvent("OnSuccessEvent").AddEventHandler(C, Delegate.CreateDelegate(T.GetEvent("OnSuccessEvent").EventHandlerType, new Sink(), "OnSuccess"));
        T.GetEvent("OnFailureEvent").AddEventHandler(C, Delegate.CreateDelegate(T.GetEvent("OnFailureEvent").EventHandlerType, new Sink(), "OnFailure"));
        T.GetMethod("CreateSvrCModule").Invoke(C, new object[] { IntPtr.Zero });

        // Phase 1: set mode 7 (80 cap) and wait until battery settles at/near its cap (<= 81)
        Log("== PHASE 1: mode=7 (80 cap), wait for battery to settle near 80 ==");
        Exec("set_charging_threshold", "{\"mode\":7}"); sleep(800);
        int cur = ReadPct();
        Log("start settle from " + cur);
        int guard = 0;
        while (cur > 81 && guard < 40)
        {
            sleep(60000);
            cur = ReadPct();
            Log("settle pct=" + cur);
            guard++;
        }
        Log("settled at " + cur);

        // Phase 2: from pinned ~80, each mode charges to its own cap; watch plateau.
        foreach (int m in new[] { 8, 5, 6, 4 })
        {
            Log("== PHASE 2: mode=" + m + " plateau watch ==");
            Exec("set_charging_threshold", "{\"mode\":" + m + "}"); sleep(800);
            LastPct = -1; FlatCount = 0;
            int p2 = ReadPct();
            Log("mode " + m + " starting pct=" + p2 + " remain=" + ReadRemain());
            for (int i = 0; i < 30 && FlatCount < 5; i++)
            {
                sleep(60000);
                int p = ReadPct();
                int r = ReadRemain();
                Log("mode " + m + " sample[" + i + "]=" + p + " remain=" + r);
            }
            Log("mode " + m + " plateaued at ~" + LastPct);
        }

        Log("== restore mode=1 ==");
        Exec("set_charging_threshold", "{\"mode\":1}"); sleep(500);
        Exec("get_charging_threshold", "{}"); sleep(300);
        Log("== done ==");
        Console.WriteLine("done");
    }

    static int ReadPct()
    {
        LastPct = -1; FlatCount = 0;
        Exec("get_battery_percent", "{}"); sleep(500);
        return LastPct;
    }
    static int ReadRemain()
    {
        Exec("get_charging_remainTime", "{}"); sleep(300);
        return 0;
    }

    static void Exec(string method, string prm)
    {
        try { T.GetMethod("Execute").Invoke(C, new object[] { "{\"method\":\"" + method + "\",\"params\":" + prm + "}", -1L }); }
        catch (Exception ex) { Log("!! THROW " + method + " : " + (ex.InnerException ?? ex).Message); }
    }
    static void sleep(int ms) { var t = new System.Threading.ManualResetEvent(false); t.WaitOne(ms); }
}

class Sink
{
    static readonly string _log = @"C:\Program Files\MI\mitool\chargingprobe\log8.txt";
    static readonly object _sync = new object();
    void W(string s) { lock (_sync) File.AppendAllText(_log, s + "\r\n"); }
    public void OnSuccess(string m, string r, long q)
    {
        if (m == "get_battery_percent")
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(r);
                if (doc.RootElement.TryGetProperty("data", out var d) && d.TryGetProperty("result", out var v))
                {
                    int pct = v.GetInt32();
                    if (pct == ChargingProbe8.GetLast()) ChargingProbe8.IncFlat();
                    else ChargingProbe8.SetLast(pct);
                }
            }
            catch { }
        }
        if (m == "get_charging_remainTime")
        {
            W("REMAIN " + r.Replace("\n", ""));
            return;
        }
        W("SUCCESS " + m + " = " + r.Replace("\n", ""));
    }
    public void OnFailure(string m, int c, string ms, long q) => W("FAIL " + m + " code=" + c + " msg=" + ms);
}