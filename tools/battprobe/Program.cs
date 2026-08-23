using System;
using System.Reflection;

class Program
{
    static object _controller;
    static MethodInfo _execute;

    static void Main(string[] args)
    {
        string method = args.Length > 0 ? args[0] : "get_battery_info";
        string parms;
        if (args.Length > 1 && int.TryParse(args[1], out int n)) parms = "{\"mode\":" + n + "}";
        else parms = args.Length > 1 ? args[1] : "{}";
        string dir = AppContext.BaseDirectory;
        var asm = Assembly.LoadFrom(System.IO.Path.Combine(dir, "SvrCModuleClrWrapper.dll"));
        var t = asm.GetType("SvrCModuleClrWrapper.ModuleController", throwOnError: true);
        _controller = t.GetMethod("GetInstance", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
        var evS = t.GetEvent("OnSuccessEvent");
        var evF = t.GetEvent("OnFailureEvent");
        evS.AddEventHandler(_controller, Delegate.CreateDelegate(evS.EventHandlerType, typeof(Program).GetMethod("OnSuccess", BindingFlags.NonPublic | BindingFlags.Static)));
        evF.AddEventHandler(_controller, Delegate.CreateDelegate(evF.EventHandlerType, typeof(Program).GetMethod("OnFailure", BindingFlags.NonPublic | BindingFlags.Static)));
        _execute = t.GetMethod("Execute");
        bool ready = (bool)t.GetMethod("CreateSvrCModule").Invoke(_controller, new object[] { IntPtr.Zero });
        Console.WriteLine("CreateSvrCModule ready=" + ready);
        if (!ready) return;
        _execute.Invoke(_controller, new object[] { "{\"method\":\"" + method + "\",\"params\":" + parms + "}", -1L });
        System.Threading.Thread.Sleep(1500);
        Console.WriteLine("done");
    }

    static void OnSuccess(string method, string response, long qid)
    {
        Console.WriteLine("  OK  " + method + " -> " + response);
    }

    static void OnFailure(string method, int code, string msg, long qid)
    {
        Console.WriteLine("  FAIL " + method + " code=" + code + " msg=" + msg);
    }
}