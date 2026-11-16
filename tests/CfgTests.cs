using System;
using System.IO;
using System.Reflection;
using Glissade3D;

static class CfgTests
{
    private static readonly string Managed = ResolveManaged();

    private static string ResolveManaged()
    {
        var ksp = Environment.GetEnvironmentVariable("KSP_DIR");
        if (string.IsNullOrEmpty(ksp)) throw new InvalidOperationException("Set KSP_DIR to the game folder");
        return Path.Combine(ksp, "KSP_x64_Data", "Managed");
    }

    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        return Run(args);
    }

    private static int Run(string[] args)
    {
        var path = args.Length > 0
            ? args[0]
            : @"E:\Games\myksp\mods\ksp-glissade3d\GameData\Glissade3D\DefaultRunways.cfg";

        var file = ConfigNode.Load(path);
        if (file == null)
        {
            Console.WriteLine("FAIL: file not parsed");
            return 1;
        }

        var nodes = file.GetNodes("Runway");
        Console.WriteLine($"runway nodes: {nodes.Length}");

        var ok = 0;
        foreach (var node in nodes)
        {
            var parsed = RunwayInfo.TryParse(node, out var info);
            Console.WriteLine(
                $"{(parsed ? "ok  " : "FAIL")} {node.GetValue("shortID")} hdg={info.HeadingDeg} alt={info.AltMsl} gs=({info.GsLat},{info.GsLon}) body={info.Body}");
            if (parsed) ok++;
        }

        var success = nodes.Length > 0 && ok == nodes.Length;
        Console.WriteLine(success ? "OK: all runway nodes parsed" : "FAIL: parse errors");
        return success ? 0 : 1;
    }

    private static Assembly Resolve(object sender, ResolveEventArgs e)
    {
        var name = new AssemblyName(e.Name).Name + ".dll";
        var candidate = Path.Combine(Managed, name);
        return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
    }
}
