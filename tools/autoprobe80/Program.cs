using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;

class AutoProbe80
{
    const string Dir = @"C:\Program Files\MI\mitool\battprobe\publish2";
    static Type T;
    static object C;
    public static int IfBatch = -1;
    public static int Pct = -1;
    public static long Rem = -1;
    static readonly object L = new object();
    static readonly string LogPath = @"C:\Program Files\MI\mitool\autoprobe80\autoprobe80.log";

    static void Log(string s) { lock (L) { Console.WriteLine(DateTime.Now.ToString("HH:mm:ss") + " " + s); File.AppendAllText(LogPath, s + "\r\n"); } }

    static void Exec(string m, string prm)
    {
        try { T.GetMethod("Execute").Invoke(C, new object[] { "{\"method\":\"" + m + "\",\"params\":" + prm + "}", -1L }); }
        catch (Exception ex) { Log("!! THROW " + m + " : " + (ex.InnerException ?? ex).Message); }
    }

    static int ReadPct() { Exec("get_battery_percent", "{}"); Thread.Sleep(500); return Pct; }
    static long ReadRem() { Exec("get_charging_remainTime", "{}"); Thread.Sleep(400); return Rem; }

    static void SetMode(int m)
    {
        Exec("set_charging_threshold", "{\"mode\":" + m + "}");
        Thread.Sleep(2500);
    }

    static int WatchStop(int mode, int start)
    {
        int neg = 0, frozen = 0, last = -1;
        for (int i = 0; i < 30; i++)
        {
            Thread.Sleep(60000);
            int p = ReadPct(); long r = ReadRem();
            Log("mode " + mode + " t+" + (i + 1) + "min pct=" + p + " remain=" + r);
            if (r < 0)
            {
                neg++;
                if (neg >= 2) { Log("mode " + mode + " STOPPED at pct=" + p); return p; }
            }
            else
            {
                neg = 0;
                if (p == last) { frozen++; if (frozen >= 6) { Log("mode " + mode + " FROZEN stop pct=" + p); return p; } }
                else { frozen = 0; last = p; }
            }
            if (p > 85) { Log("mode " + mode + " too high, abort"); return -1; }
        }
        Log("mode " + mode + " timeout, last pct=" + last);
        return -1;
    }

    static void Main(string[] args)
    {
        File.WriteAllText(LogPath, "=== autoprobe80 start ===\r\n");
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

        Log("module ready");
        Exec("set_charging_protect", "{\"mode\":1}");
        Exec("register_battery_notify", "{}");

        var modes = new int[0];
        bool immediate = false;
        foreach (var a in args)
        {
            if (a == "immediate") { immediate = true; continue; }
            if (int.TryParse(a, out var m)) { Array.Resize(ref modes, modes.Length + 1); modes[modes.Length - 1] = m; }
        }
        if (modes.Length == 0) modes = new int[] { 6, 7, 8 };
        Log("probe modes=[" + string.Join(",", modes) + "] immediate=" + immediate);

        if (!immediate)
        {
            Log("waiting for charging && pct<=79 ...");
            int waitStart = DateTime.Now.Hour * 60 + DateTime.Now.Minute;
            int iter = 0;
            while (true)
            {
                int p = ReadPct(); long r = ReadRem();
                Log("wait ifBatch=" + IfBatch + " pct=" + p + " remain=" + r);
                if (IfBatch > 0 && p >= 0 && p <= 79 && r > 0) break;
                if (p < 40) { Log("battery too low abort"); return; }
                int now = DateTime.Now.Hour * 60 + DateTime.Now.Minute;
                if (now < waitStart) now += 1440;
                if (now - waitStart > 720) { Log("wait timeout 12h abort"); return; }
                iter++;
                Thread.Sleep(60000);
            }
        }
        else
        {
            Log("immediate mode; current pct=" + ReadPct() + " remain=" + ReadRem());
        }
        SetMode(modes[0]);
        Log("start with mode " + modes[0]);

        var probeModes = modes;
        foreach (var mode in probeModes)
        {
            Log("---- set mode=" + mode + " ----");
            SetMode(mode);
            int start = ReadPct();
            Log("mode " + mode + " startPct=" + start);
            int stop = WatchStop(mode, start);
            if (stop > 0) Log("RESULT mode=" + mode + " stopPct=" + stop);
        }

        Log("=== choosing best 80 alias ===");
        int best = 6;
        int bestDist = 99;
        foreach (var mode in modes)
        {
            int stop = ReadStop(mode);
            if (stop <= 0) continue;
            int d = Math.Abs(stop - 80);
            Log("candidate mode=" + mode + " stopPct=" + stop + " dist=" + d);
            if (d < bestDist) { bestDist = d; best = mode; }
        }
        Log("BEST mode=" + best + " dist=" + bestDist);
        SetMode(best);
        Thread.Sleep(2000);
        Log("final threshold set mode=" + best + " pct=" + ReadPct() + " remain=" + ReadRem());
        Log("=== autoprobe80 done ===");
        Environment.Exit(0);
    }

    static int ReadStop(int mode)
    {
        string line = null;
        foreach (var l in File.ReadAllLines(LogPath))
            if (l.StartsWith("RESULT mode=" + mode)) line = l;
        if (line == null) return -1;
        int i = line.LastIndexOf("stopPct=");
        return int.Parse(line.Substring(i + 8));
    }
}

class Sink
{
    public void OnSuccess(string m, string r, long q)
    {
        try
        {
            using var doc = JsonDocument.Parse(r);
            if (!doc.RootElement.TryGetProperty("data", out var d)) return;
            if (m == "get_battery_percent" && d.TryGetProperty("result", out var v)) AutoProbe80.Pct = v.GetInt32();
            else if (m == "get_charging_remainTime" && d.TryGetProperty("result", out var r2)) AutoProbe80.Rem = r2.GetInt64();
            else if (m == "register_battery_notify" && d.TryGetProperty("if_battery", out var i)) AutoProbe80.IfBatch = i.GetInt32();
        }
        catch { }
    }
    public void OnFailure(string m, int c, string ms, long q) => File.AppendAllText(@"C:\Program Files\MI\mitool\autoprobe80\autoprobe80.log", "FAIL " + m + " code=" + c + "\r\n");
}