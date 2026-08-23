using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

var wrapperPath = @"C:\Program Files\MI\XiaomiPCManager\5.8.1.121\SvrCModuleClrWrapper.dll";
var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location);

var paths = new List<string>();
var core = new[] { "System.Private.CoreLib.dll", "System.Runtime.dll", "System.Console.dll",
                   "mscorlib.dll", "netstandard.dll", "System.Collections.dll", "System.Linq.dll",
                   "System.Text.Json.dll", "System.Threading.dll" };
foreach (var c in core) { var p = Path.Combine(runtimeDir, c); if (File.Exists(p)) paths.Add(p); }
paths.Add(wrapperPath);

var resolver = new PathAssemblyResolver(paths);
using var mlc = new MetadataLoadContext(resolver);

var asm = mlc.LoadFromAssemblyPath(wrapperPath);
var sb = new System.Text.StringBuilder();
foreach (var t in asm.GetTypes().OrderBy(t => t.FullName))
{
    if (!t.IsPublic && !t.IsNestedPublic) continue;
    sb.AppendLine("TYPE " + t.FullName);
    foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                       .Where(m => !m.IsSpecialName || m.Name.StartsWith("get_") || m.Name.StartsWith("set_"))
                       .OrderBy(m => m.Name))
    {
        var ps = string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name));
        sb.AppendLine("   " + m.Name + "(" + ps + ") : " + m.ReturnType.Name);
    }
    foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                       .OrderBy(p => p.Name))
    {
        sb.AppendLine("   PROP " + p.Name + " : " + p.PropertyType.Name);
    }
    foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                       .OrderBy(f => f.Name))
    {
        sb.AppendLine("   FIELD " + f.Name + " : " + f.FieldType.Name + " = " + (f.IsLiteral ? f.GetRawConstantValue() : "?"));
    }
}
File.WriteAllText(@"C:\Program Files\MI\mitool\wrapapi.txt", sb.ToString(), new System.Text.UTF8Encoding(true));
Console.WriteLine("done " + asm.FullName);