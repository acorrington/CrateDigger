using System.Reflection;
using System.Runtime.Loader;

// Round-22: MusicVideo type + BaseItem video metadata shapes.
var sysDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Emby-Server", "system");

var alc = new AssemblyLoadContext("probe22", isCollectible: false);
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

var controller = alc.LoadFromAssemblyPath(Path.Combine(sysDir, "MediaBrowser.Controller.dll"));
IEnumerable<Type> types;
try { types = controller.GetTypes(); }
catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null)!; }

var mv = types.FirstOrDefault(t => t.Name == "MusicVideo");
Console.WriteLine($"MusicVideo: {mv?.FullName ?? "NOT FOUND"}  base={mv?.BaseType?.FullName}");
if (mv != null)
{
    foreach (var p in mv.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        Console.WriteLine($"  declared: {p.PropertyType.Name} {p.Name}");
    Console.WriteLine("  interfaces: " + string.Join(",", mv.GetInterfaces().Select(i => i.Name)));
}

var bi = types.First(t => t.Name == "BaseItem" && t.Namespace == "MediaBrowser.Controller.Entities");
Console.WriteLine("=== BaseItem video-ish props ===");
foreach (var p in bi.GetProperties(BindingFlags.Public | BindingFlags.Instance)
             .Where(p => p.Name is "ProductionYear" or "Directors" or "Tags" or "Overview" or "Artists" or "People" or "Studios"))
    Console.WriteLine($"  {p.PropertyType.FullName} {p.Name}");

// and what does MusicVideo inherit for artists (if nothing declared, check interfaces on BaseItem)
Console.WriteLine("=== BaseItem interfaces mentioning Artist ===");
Console.WriteLine("  " + string.Join(",", bi.GetInterfaces().Where(i => i.Name.Contains("Artist")).Select(i => i.Name)));

// Video class
var vid = types.FirstOrDefault(t => t.Name == "Video");
Console.WriteLine($"Video: {vid?.FullName}  base={vid?.BaseType?.FullName}");
if (vid != null)
    foreach (var p in vid.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Take(12))
        Console.WriteLine($"  declared: {p.PropertyType.Name} {p.Name}");