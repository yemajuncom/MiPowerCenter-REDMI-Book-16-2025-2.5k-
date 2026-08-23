using System;
using System.IO;
using System.Reflection;
using System.Threading;

class SetOne
{
    const string Dir = @"C:\Program Files\MI\mitool\battprobe\publish2";
    static Type T;
    static object C;
    static readonly string RespLog = @"C:\Program Files\MI\mitool\setone\resp.log";

    static void Exec(string m, string prm)
    {
        try { T.GetMethod("Execute").Invoke(C, new object[] { "{\"method\":\"" + m + "\",\"params\":" + prm + "}", -1L }); }
        catch (Exception ex) { File.AppendAllText(RespLog, "THROW " + m + " : " + (ex.InnerException ?? ex).Message + "\r\n"); }
    }

    static void Main(string[] args)
    {
        int mode = int.Parse(args.Length > 0 ? args[0] : "2");
        bool toggle = args.Length > 1 && args[1] == "toggle";
        File.WriteAllText(RespLog, "=== setone mode=" + mode + " toggle=" + toggle + " ===\r\n");
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
        Thread.Sleep(1500);
        if (toggle)
        {
            File.AppendAllText(RespLog, "[toggle] protect off\r\n");
            Exec("set_charging_protect", "{\"mode\":0}");
            Thread.Sleep(1200);
            Exec("set_charging_threshold", "{\"mode\":" + mode + "}");
            Thread.Sleep(1500);
            File.AppendAllText(RespLog, "[toggle] protect on\r\n");
            Exec("set_charging_protect", "{\"mode\":1}");
            Thread.Sleep(2500);
        }
        else
        {
            Exec("set_charging_protect", "{\"mode\":1}");
            Thread.Sleep(800);
            Exec("set_charging_threshold", "{\"mode\":" + mode + "}");
            Thread.Sleep(2500);
            Exec("get_charging_threshold", "{}");
            Thread.Sleep(1500);
        }
        File.AppendAllText(RespLog, "=== setone done ===\r\n");
        Environment.Exit(0);
    }
}

class Sink
{
    static readonly object L = new object();
    public void OnSuccess(string m, string r, long q) { lock (L) File.AppendAllText(@"C:\Program Files\MI\mitool\setone\resp.log", m + " => " + r + "\r\n"); }
    public void OnFailure(string m, int c, string ms, long q) => File.AppendAllText(@"C:\Program Files\MI\mitool\setone\resp.log", "FAIL " + m + " code=" + c + " msg=" + ms + "\r\n");
}