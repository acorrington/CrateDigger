using System.Reflection;
using System.Runtime.Loader;

// Round-14: the ImageFormat enum (member names for IHasThumbImage.ThumbImageFormat).
var sysDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Emby-Server", "system");

var alc = new AssemblyLoadContext("probe14", isCollectible: false);
alc.Resolving += (ctx, name) =>
{
    var simple = name.Name + ".dll";
    var p = Path.Combine(sysDir, simple);
    if (File.Exists(p)) return ctx.LoadFromAssemblyPath(p);
    var rt = Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, simple);
    if (File.Exists(rt)) return ctx.LoadFromAssemblyPath(rt);
    return null;
};

foreach (var asmName in new[] { "MediaBrowser.Model.dll", "MediaBrowser.Common.dll", "MediaBrowser.Controller.dll" })
{
    var asm = alc.LoadFromAssemblyPath(Path.Combine(sysDir, asmName));
    IEnumerable<Type> types;
    try { types = asm.GetTypes(); }
    catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null)!; }

    foreach (var t in types.Where(t => t.Name == "ImageFormat" && t.IsEnum))
    {
        Console.WriteLine($"{asmName} :: {t.FullName}");
        foreach (var n in Enum.GetNames(t))
            Console.WriteLine($"  {n} = {Convert.ToInt32(Enum.Parse(t, n))}");
    }
}

// Confirm the property's return type resolves to that enum
var common2 = alc.LoadFromAssemblyPath(Path.Combine(sysDir, "MediaBrowser.Common.dll"));
IEnumerable<Type> cTypes;
try { cTypes = common2.GetTypes(); }
catch (ReflectionTypeLoadException ex) { cTypes = ex.Types.Where(t => t != null)!; }
var iface = cTypes.First(t => t.Name == "IHasThumbImage");
var prop = iface.GetProperty("ThumbImageFormat")!;
Console.WriteLine($"ThumbImageFormat property type: {prop.PropertyType.FullName} (enum={prop.PropertyType.IsEnum})");
Console.WriteLine($"GetThumbImage returns: {iface.GetMethod("GetThumbImage")!.ReturnType.FullName}");