using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MiPowerCenter.Services;

public sealed class XiaomiModuleAdapter
{
    public event Action<string, string, long> Success;
    public event Action<string, int, string, long> Failure;

    private object _controller;
    private MethodInfo _execute;
    private bool _ready;
    private string _dir;

    public string Dir => _dir;
    public bool IsReady => _ready;

    public static string FindXiaomiDir()
    {
        // Self-contained: if the C++/CLI wrapper is bundled next to this app, use it.
        string own = AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(own, "SvrCModuleClrWrapper.dll")) && File.Exists(Path.Combine(own, "SvrCModule.dll")))
            return own;

        string root = @"C:\Program Files\MI\XiaomiPCManager";
        if (!Directory.Exists(root)) return null;
        Version best = null; string bestDir = null;
        foreach (string d in Directory.GetDirectories(root))
        {
            if (!File.Exists(Path.Combine(d, "SvrCModuleClrWrapper.dll")) || !File.Exists(Path.Combine(d, "SvrCModule.dll")))
                continue;
            string name = Path.GetFileName(d);
            if (Version.TryParse(name, out Version v) && (best == null || v > best)) { best = v; bestDir = d; }
            else if (best == null) bestDir = d;
        }
        if (bestDir == null && File.Exists(Path.Combine(root, "SvrCModuleClrWrapper.dll"))) bestDir = root;
        return bestDir;
    }

    public void Init(string dir)
    {
        _dir = dir;
        AppLog.Write("Init dir=" + dir);
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            string name = new AssemblyName(e.Name).Name + ".dll";
            string p = Path.Combine(dir, name);
            if (File.Exists(p)) return Assembly.LoadFrom(p);
            return null;
        };
        AddDllDirectory(dir);
        Environment.SetEnvironmentVariable("PATH", dir + ";" + Environment.GetEnvironmentVariable("PATH"));
        try { Environment.CurrentDirectory = dir; } catch { }

        var asm = Assembly.LoadFrom(Path.Combine(dir, "SvrCModuleClrWrapper.dll"));
        var t = asm.GetType("SvrCModuleClrWrapper.ModuleController", throwOnError: true);
        _controller = t.GetMethod("GetInstance", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);

        var evS = t.GetEvent("OnSuccessEvent");
        var evF = t.GetEvent("OnFailureEvent");
        evS.AddEventHandler(_controller, Delegate.CreateDelegate(evS.EventHandlerType, this, "OnSuccess"));
        evF.AddEventHandler(_controller, Delegate.CreateDelegate(evF.EventHandlerType, this, "OnFailure"));

        _execute = t.GetMethod("Execute");
        _ready = (bool)t.GetMethod("CreateSvrCModule").Invoke(_controller, new object[] { IntPtr.Zero });
        AppLog.Write("CreateSvrCModule ready=" + _ready);
    }

    public void Execute(string json)
    {
        if (!_ready || _execute == null) return;
        _execute.Invoke(_controller, new object[] { json, -1L });
    }

    /// <summary>
    /// 性能模式（workLoad）依赖 MiDeviceService 服务，该服务随小米电脑管家卸载一起被删除。
    /// 本应用已在自身目录 Timi\MiDeviceService\ 内置完整运行时（自包含），
    /// 这里负责：服务不存在时重新注册、路径失效时改指内置副本、确保自动启动且已运行，
    /// 从而实现卸载管家后仍可无管家使用（含性能模式）。
    /// </summary>
    public static void EnsureTimiServices()
    {
        string bundle = Path.Combine(AppContext.BaseDirectory, "Timi", "MiDeviceService", "MiDeviceService.exe");
        if (!File.Exists(bundle)) bundle = null;

        EnsureService("MiDeviceService", bundle);
        EnsureAutoStart("MiDeviceService");
        bool started = TryStart("MiDeviceService");
        if (started) Thread.Sleep(3000); // 等待管道就绪
    }

    private static void EnsureService(string name, string bundleExe)
    {
        try
        {
            string key = @"SYSTEM\CurrentControlSet\Services\" + name;
            using var reg = Registry.LocalMachine.OpenSubKey(key);
            if (reg == null)
            {
                if (bundleExe == null) { AppLog.Write("EnsureService " + name + ": missing and no bundled runtime"); return; }
                string r = RunSc("create " + name + " binPath= \"" + bundleExe + "\" start= auto");
                AppLog.Write("EnsureService create " + name + " -> " + r.Replace("\r", " ").Replace("\n", " "));
                return;
            }
            string img = reg.GetValue("ImagePath") as string;
            bool valid = !string.IsNullOrEmpty(img) && File.Exists(UnquotePath(img));
            if (!valid && bundleExe != null)
            {
                using var k = Registry.LocalMachine.OpenSubKey(key, writable: true);
                k.SetValue("ImagePath", bundleExe, RegistryValueKind.String);
                AppLog.Write("EnsureService repoint " + name + " ImagePath -> " + bundleExe);
            }
        }
        catch (Exception ex) { AppLog.Write("EnsureService " + name + ": " + ex.Message); }
    }

    private static string UnquotePath(string s)
    {
        s = (s ?? "").Trim();
        if (s.Length > 1 && s[0] == '"' && s[^1] == '"') return s[1..^1];
        return s;
    }

    private static void EnsureAutoStart(string name)
    {
        try
        {
            string key = @"SYSTEM\CurrentControlSet\Services\" + name;
            using var k = Registry.LocalMachine.OpenSubKey(key, writable: true);
            if (k != null)
            {
                object v = k.GetValue("Start");
                int start = v is int i ? i : (v is byte b1 ? b1 : -1);
                if (start != 2) { k.SetValue("Start", 2, RegistryValueKind.DWord); AppLog.Write("EnsureAutoStart set " + name + " Start=2"); }
            }
        }
        catch (Exception ex) { AppLog.Write("EnsureAutoStart " + name + ": " + ex.Message); }
    }

    private static bool TryStart(string name)
    {
        try
        {
            string q = RunSc("query " + name);
            if (!q.Contains("SERVICE_NAME")) { AppLog.Write("TryStart " + name + " not installed"); return false; }
            if (q.Contains("RUNNING")) return false;
            AppLog.Write("TryStart service " + name + " (state=" + q.Replace("\r", "").Replace("\n", " ") + ")");
            string r = RunSc("start " + name);
            AppLog.Write("sc start " + name + " -> " + r.Replace("\r", "").Replace("\n", " "));
            return !r.Contains("1058"); // 1058 = disabled（需重启生效），启动仍失败
        }
        catch (Exception ex) { AppLog.Write("TryStart " + name + ": " + ex.Message); return false; }
    }

    private static string RunSc(string args)
    {
        var psi = new ProcessStartInfo("sc.exe", args)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi);
        p.WaitForExit(8000);
        return p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
    }

    public void OnSuccess(string method, string response, long queryId)
    {
        AppLog.Write("SUCCESS method=" + method + " qid=" + queryId + " resp=" + response);
        Success?.Invoke(method, response, queryId);
    }

    public void OnFailure(string method, int code, string msg, long queryId)
    {
        AppLog.Write("FAIL method=" + method + " code=" + code + " msg=" + msg);
        Failure?.Invoke(method, code, msg, queryId);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr AddDllDirectory(string dirPath);
}