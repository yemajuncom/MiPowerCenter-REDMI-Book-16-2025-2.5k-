using System;
using System.IO;
using System.Reflection;

class ChgWatch
{
    const string Dir = @"C:\Program Files\MI\MiPowerCenter";
    static Type T;
    static object C;
    public static int Pct = -1;
    public static long Rem = -1;
    static readonly object L = new object();
    static readonly string LogPath = @"C:\Program Files\MI\mitool\chgwatch\watch.log";

    static void Log(string s) { lock (L) File.AppendAllText(LogPath, s + "\r\n"); }

    static void Exec(string m, string prm)
    {
        try { T.GetMethod("Execute").Invoke(C, new object[] { "{\"method\":\"" + m + "\",\"params\":" + prm + "}", -1L }); }
        catch (Exception ex) { Log("!! THROW " + m + " : " + (ex.InnerException ?? ex).Message); }
    }

    static int ReadPct() { Exec("get_battery_percent", "{}"); System.Threading.Thread.Sleep(700); return Pct; }
    static long ReadRem() { Exec("get_charging_remainTime", "{}"); System.Threading.Thread.Sleep(500); return Rem; }

    static void SetMode(int m)
    {
        Exec("set_charging_threshold", "{\"mode\":" + m + "}");
        System.Threading.Thread.Sleep(2500);
    }

    static void Main()
    {
        File.Delete(LogPath);
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            string n = new AssemblyName(e.Name).Name + ".dll";
            string p = Path.Combine(Dir, n);
            return File.Exists(p) ? Assembly.LoadFrom(p) : null;
        };
        var asm = Assembly.LoadFrom(Path.Combine(Dir, "SvrCModuleClrWrapper.dll"));
        T = asm.GetType("SvrCModuleClrWrapper.ModuleController");
        C = T.GetMethod("GetInstance", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
        T.GetEvent("OnSuccessEvent").AddEventHandler(C, Delegate.CreateDelegate(T.GetEvent("OnSuccessEvent").EventHandlerType, new Sink(), "OnSuccess"));
        T.GetEvent("OnFailureEvent").AddEventHandler(C, Delegate.CreateDelegate(T.GetEvent("OnFailureEvent").EventHandlerType, new Sink(), "OnFailure"));
        T.GetMethod("CreateSvrCModule").Invoke(C, new object[] { IntPtr.Zero });

        Log("=== chgwatch start ===");
        Exec("set_charging_protect", "{\"mode\":1}");
        System.Threading.Thread.Sleep(2000);
        Log("start pct=" + ReadPct() + " remain=" + ReadRem());

        foreach (int m in new[] { 2, 3 })
        {
            Log("-- watch mode=" + m);
            SetMode(m);
            int stopPct = -1; int negCount = 0;
            for (int i = 0; i < 25; i++)
            {
                System.Threading.Thread.Sleep(60000);
                int p = ReadPct();
                long r = ReadRem();
                Log("mode " + m + " t+" + (i + 1) + "min pct=" + p + " remain=" + r);
                if (r < 0)
                {
                    negCount++;
                    stopPct = p;
                    if (negCount >= 2) { Log("mode " + m + " STOPPED at pct=" + stopPct); break; }
                }
                else negCount = 0;
            }
            Log("mode " + m + " end state pct=" + stopPct + " (negCount=" + negCount + ")");
        }

        Log("=== restoring mode=4 ===");
        SetMode(4);
        Log("final pct=" + ReadPct() + " remain=" + ReadRem());
        Log("=== done ===");
        Console.WriteLine("done");
        Environment.Exit(0);
    }
}

class Sink
{
    static readonly object _sync = new object();
    void W(string s) { lock (_sync) File.AppendAllText(@"C:\Program Files\MI\mitool\chgwatch\watch.log", s + "\r\n"); }
    public void OnSuccess(string m, string r, long q)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(r);
            if (!doc.RootElement.TryGetProperty("data", out var d)) return;
            if (m == "get_battery_percent" && d.TryGetProperty("result", out var v)) ChgWatch.Pct = v.GetInt32();
            else if (m == "get_charging_remainTime" && d.TryGetProperty("result", out var r2)) ChgWatch.Rem = r2.GetInt64();
        }
        catch { }
    }
    public void OnFailure(string m, int c, string ms, long q) => W("FAIL " + m + " code=" + c + " msg=" + ms);
}