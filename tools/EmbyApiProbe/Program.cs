using System.Reflection;
using System.Runtime.Loader;

// Round-10: find how the Dashboard discovers & serves plugin config pages.
var libDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "lib"));
var sysDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Emby-Server", "system");

var alc = new AssemblyLoadContext("probe10", isCollectible: false);
alc.Resolving += (ctx, name) =>
{
    var simple = name.Name + ".dll";
    foreach (var dir in new[] { Path.GetDirectoryName(typeof(object).Assembly.Location)!, libDir, sysDir })
    {
        var p = Path.Combine(dir, simple);
        if (File.Exists(p)) return ctx.LoadFromAssemblyPath(p);
    }
    return null;
};

IEnumerable<Type> Safe(Assembly a)
{
    try { return a.GetTypes(); }
    catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null)!; }
}

// All server assemblies that could carry routes
foreach (var asmName in new[] { "Emby.Web.dll", "EmbyServer.dll", "Emby.Api.dll", "Emby.Server.Implementations.dll" })
{
    var path = Path.Combine(sysDir, asmName);
    if (!File.Exists(path)) continue;
    Assembly asm;
    try { asm = alc.LoadFromAssemblyPath(path); } catch { continue; }

    foreach (var t in Safe(asm))
    {
        foreach (var a in t.GetCustomAttributes(false).Where(a => a.GetType().Name == "RouteAttribute"))
        {
            var p = (string)a.GetType().GetProperty("Path")?.GetValue(a);
            var v = (string)a.GetType().GetProperty("Verbs")?.GetValue(a);
            if (p != null && (p.Contains("page", StringComparison.OrdinalIgnoreCase) ||
                              p.Contains("config", StringComparison.OrdinalIgnoreCase) ||
                              p.Contains("dashboard", StringComparison.OrdinalIgnoreCase)))
                Console.WriteLine($"{asmName} :: {t.FullName}  [{p}] {v}");
        }
    }
}

// Methods on services that mention PluginPage/IHasWebPages (how pages are listed)
Console.WriteLine("--- services reading IHasWebPages/PluginPageInfo ---");
foreach (var asmName in new[] { "Emby.Web.dll", "EmbyServer.dll", "Emby.Server.Implementations.dll" })
{
    var path = Path.Combine(sysDir, asmName);
    if (!File.Exists(path)) continue;
    Assembly asm;
    try { asm = alc.LoadFromAssemblyPath(path); } catch { continue; }
    foreach (var t in Safe(asm))
    {
        foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            if (m.Name.Contains("PluginPage", StringComparison.OrdinalIgnoreCase) ||
                m.Name.Contains("ConfigurationPage", StringComparison.OrdinalIgnoreCase) ||
                m.GetParameters().Any(pt => pt.ParameterType.Name.Contains("PluginPage")))
                Console.WriteLine($"  {asmName} :: {t.FullName}.{m.Name}({string.Join(",", m.GetParameters().Select(x => x.ParameterType.Name))}) -> {m.ReturnType.Name}");
        }
    }
}