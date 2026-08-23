using System;
using System.IO;
using System.Reflection;

class CapScan
{
    const string Dir = @"C:\Program Files\MI\MiPowerCenter";
    static Type T;
    static object C;
    public static int Pct = -1;
    public static long Rem = -1;
    static readonly object L = new object();
    static readonly string LogPath = @"C:\Program Files\MI\mitool\chargingcalib\scan.log";

    static void Log(string s) { lock (L) File.AppendAllText(LogPath, s + "\r\n"); }

    static int ReadPct() { Exec("get_battery_percent", "{}"); System.Threading.Thread.Sleep(700); return Pct; }
    static long ReadRem() { Exec("get_charging_remainTime", "{}"); System.Threading.Thread.Sleep(500); return Rem; }

    static void Exec(string m, string prm)
    {
        try { T.GetMethod("Execute").Invoke(C, new object[] { "{\"method\":\"" + m + "\",\"params\":" + prm + "}", -1L }); }
        catch (Exception ex) { Log("!! THROW " + m + " : " + (ex.InnerException ?? ex).Message); }
    }

    static void Sample(int mode)
    {
        Exec("set_charging_threshold", "{\"mode\":" + mode + "}");
        System.Threading.Thread.Sleep(2000);
        int p = ReadPct();
        long r = ReadRem();
        Log("mode=" + mode + " pct=" + p + " remain=" + r);
    }

    static void Main()
    {
        File.Delete(LogPath);
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            string n = new AssemblyName(e.Name).Name + ".dll";
            string pth = Path.Combine(Dir, n);
            return File.Exists(pth) ? Assembly.LoadFrom(pth) : null;
        };
        var asm = Assembly.LoadFrom(Path.Combine(Dir, "SvrCModuleClrWrapper.dll"));
        T = asm.GetType("SvrCModuleClrWrapper.ModuleController");
        C = T.GetMethod("GetInstance", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
        T.GetEvent("OnSuccessEvent").AddEventHandler(C, Delegate.CreateDelegate(T.GetEvent("OnSuccessEvent").EventHandlerType, new Sink(), "OnSuccess"));
        T.GetEvent("OnFailureEvent").AddEventHandler(C, Delegate.CreateDelegate(T.GetEvent("OnFailureEvent").EventHandlerType, new Sink(), "OnFailure"));
        T.GetMethod("CreateSvrCModule").Invoke(C, new object[] { IntPtr.Zero });

        Log("=== capscan start, ensure protect on ===");
        Exec("set_charging_protect", "{\"mode\":1}");
        System.Threading.Thread.Sleep(2000);

        // require charging to be ACTIVE (remain > 0) before sampling makes sense
        int probePct = ReadPct();
        long probeRem = ReadRem();
        Log("initial pct=" + probePct + " remain=" + probeRem + " (wait for AC / charging)");
        int waitGuard = 0;
        while (probeRem <= 0 && waitGuard < 60)
        {
            System.Threading.Thread.Sleep(30000);
            probePct = ReadPct();
            probeRem = ReadRem();
            Log("waiting.. pct=" + probePct + " remain=" + probeRem);
            waitGuard++;
        }
        if (probeRem <= 0)
        {
            Log("=== NO CHARGING after 30 min, aborting ===");
            Console.WriteLine("NOCHARGING");
            Environment.Exit(0);
        }

        int[] modes = { 2, 3, 4, 5, 6, 7, 8, 9 };
        for (int round = 0; round < 3; round++)
        {
            Log("-- round " + round + " start pct=" + ReadPct());
            foreach (int m in modes)
            {
                Sample(m);
                Sample(4);
                System.Threading.Thread.Sleep(3000);
            }
        }

        Log("=== restoring mode=4 (known 90%) ===");
        Sample(4);
        Log("=== done ===");
        Console.WriteLine("done");
        Environment.Exit(0);
    }
}

class Sink
{
    static readonly object _sync = new object();
    void W(string s) { lock (_sync) File.AppendAllText(@"C:\Program Files\MI\mitool\chargingcalib\scan.log", s + "\r\n"); }
    public void OnSuccess(string m, string r, long q)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(r);
            if (!doc.RootElement.TryGetProperty("data", out var d)) return;
            if (m == "get_battery_percent" && d.TryGetProperty("result", out var v)) CapScan.Pct = v.GetInt32();
            else if (m == "get_charging_remainTime" && d.TryGetProperty("result", out var r2)) CapScan.Rem = r2.GetInt64();
        }
        catch { }
    }
    public void OnFailure(string m, int c, string ms, long q) => W("FAIL " + m + " code=" + c + " msg=" + ms);
}
