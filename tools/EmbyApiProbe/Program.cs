using System.Reflection;
using System.Runtime.Loader;

// Round-21: full call graph of the working GetItems handler -> exact query setters.
var sysDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Emby-Server", "system");

var alc = new AssemblyLoadContext("probe21", isCollectible: false);
alc.Resolving += (ctx, name) =>
{
    var simple = name.Name + ".dll";
    foreach (var dir in new[] { Path.GetDirectoryName(typeof(object).Assembly.Location)!, sysDir })
    {
        var p = Path.Combine(dir, simple);
        if (File.Exists(p)) return ctx.LoadFromAssemblyPath(p);
    }
    return null;
};
foreach (var dll in Directory.GetFiles(sysDir, "*.dll"))
{
    try { alc.LoadFromAssemblyPath(dll); } catch { }
}

IEnumerable<Type> Safe(Assembly a)
{
    try { return a.GetTypes(); }
    catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null)!; }
    catch { return Array.Empty<Type>(); }
}

byte[]? Body(MethodBase m)
{
    try { return m.GetMethodBody()?.GetILAsByteArray(); } catch { return null; }
}

var controller = alc.LoadFromAssemblyPath(Path.Combine(sysDir, "MediaBrowser.Controller.dll"));
var q = Safe(controller).First(t => t.Name == "InternalItemsQuery");
var api = alc.LoadFromAssemblyPath(Path.Combine(sysDir, "Emby.Api.dll"));

// 1. Find the handler: method whose parameter type is Emby.Api.UserLibrary.GetItems
Console.WriteLine("=== handler(s) for GET /Items ===");
var targets = new List<MethodInfo>();
foreach (var t in Safe(api))
{
    foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
    {
        ParameterInfo[] ps;
        try { ps = m.GetParameters(); } catch { continue; }
        if (ps.Length == 1 && ps[0].ParameterType.FullName == "Emby.Api.UserLibrary.GetItems")
        {
            Console.WriteLine($"  {t.FullName}.{m.Name}");
            targets.Add(m);
            // async state machine?
            var nested = t.GetNestedTypes(BindingFlags.NonPublic).FirstOrDefault(n => n.Name.Contains(m.Name) && n.Name.Contains("d__"));
            if (nested != null)
            {
                var mv = nested.GetMethod("MoveNext", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (mv != null) { Console.WriteLine($"    (state machine {nested.Name}.MoveNext)"); targets.Add(mv); }
            }
        }
    }
}

// 2. ALL methodrefs from the handler(s) + helpers one level deep
Console.WriteLine("=== methodrefs (handler + one helper level) ===");
var visited = new HashSet<string>();
var queue = new Queue<MethodInfo>(targets);
while (queue.Count > 0)
{
    var m = queue.Dequeue();
    var body = Body(m);
    if (body == null) continue;
    for (var i = 0; i < body.Length - 4; i++)
    {
        if (body[i] != 0x28 && body[i] != 0x6f) continue;
        var token = BitConverter.ToInt32(body, i + 1);
        if ((token & 0xFF000000) != 0x0A000000) continue;
        MethodInfo mr;
        try { mr = (MethodInfo)m.Module.ResolveMethod(token); } catch { continue; }
        var line = $"{m.DeclaringType?.Name}.{m.Name} -> {mr.DeclaringType?.Name}::{mr.Name}({string.Join(",", mr.GetParameters().Select(p => p.ParameterType.Name))})";
        if (!visited.Add(line)) continue;

        var isQuerySetter = mr.DeclaringType == q;
        var isParentHelper = mr.Name.Contains("Parent", StringComparison.OrdinalIgnoreCase) ||
                             (mr.Name.Contains("Query", StringComparison.OrdinalIgnoreCase) && mr.IsStatic);
        if (isQuerySetter || isParentHelper)
            Console.WriteLine($"  {line}{(isQuerySetter ? "   *** QUERY SETTER ***" : "")}");

        // recurse one level into static helpers that look query-related
        if (mr.IsStatic && mr.DeclaringType != q && isParentHelper && visited.Count < 400)
            queue.Enqueue(mr);
    }
}

// 3. LibraryManager.SetUserAndParents body — normalization truth
Console.WriteLine("=== LibraryManager.SetUserAndParents (normalization truth) ===");
var lmType = Safe(controller).FirstOrDefault(t => t.Name == "LibraryManager");
var sup = lmType?.GetMethod("SetUserAndParents", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
if (sup != null)
{
    var b = Body(sup);
    if (b != null)
    {
        for (var i = 0; i < b.Length - 4; i++)
        {
            if (b[i] == 0x72)
            {
                try { Console.WriteLine($"  ldstr \"{lmType.Assembly.ManifestModule.ResolveString(BitConverter.ToInt32(b, i + 1))}\""); } catch { }
            }
            else if (b[i] == 0x28 || b[i] == 0x6f)
            {
                var token = BitConverter.ToInt32(b, i + 1);
                if ((token & 0xFF000000) != 0x0A000000) continue;
                try
                {
                    var mr = (MethodInfo)lmType.Assembly.ManifestModule.ResolveMethod(token);
                    Console.WriteLine($"  call {mr.DeclaringType?.Name}::{mr.Name}({string.Join(",", mr.GetParameters().Select(p => p.ParameterType.Name))})");
                }
                catch { }
            }
        }
    }
    else Console.WriteLine("  (body unavailable)");
}
else Console.WriteLine("  method not found");