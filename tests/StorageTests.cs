using System;
using System.IO;
using System.Reflection;
using Glissade3D;

static class StorageTests
{
    private static int _failed;
    private static readonly string Managed = ResolveManaged();
    private static string _root;

    public static int Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;

        _root = Path.Combine(Path.GetTempPath(), "glissade-storage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        try
        {
            TestSlug();
            TestUniqueStem();
            TestSaveLoad();
            TestSameName();
            TestRename();
            TestRenameConflict();
            TestDelete();
            TestMultiImport();
            TestMigration();
            TestDuplicateId();
            TestRunwayImport();
            TestSettingsUrl();
        }
        catch (Exception e)
        {
            _failed++;
            Console.WriteLine("FAIL exception: " + e);
        }
        finally
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        Console.WriteLine(_failed == 0 ? "OK: all storage checks passed" : $"FAILED: {_failed}");
        return _failed == 0 ? 0 : 1;
    }

    private static void TestSettingsUrl()
    {
        var settings = new GatesSettings { SharedBaseUrl = "http://127.0.0.1:8765/" };
        var node = settings.ToNode();
        var stored = node.GetValue("sharedBaseUrl");
        ExpectTrue("settings url is escaped", !string.IsNullOrEmpty(stored) && stored.IndexOf("//", StringComparison.Ordinal) < 0);
        var parsed = ConfigNode.Parse(node.ToString());
        var root = parsed.HasNode("Glissade3D") ? parsed.GetNode("Glissade3D") : parsed;
        var loaded = new GatesSettings();
        loaded.ApplyNode(root);
        ExpectTrue("settings url restored", loaded.SharedBaseUrl == "http://127.0.0.1:8765/");
    }

    private static void TestSlug()
    {
        ExpectTrue("slug spaces", RouteNaming.Slug("Bridge over river", "id") == "bridge-over-river");
        ExpectTrue("slug cyrillic", RouteNaming.Slug("Остров", "id") == "остров");
        ExpectTrue("slug punctuation", RouteNaming.Slug("a/b^c", "id") == "a-b-c");
        ExpectTrue("slug trailing", RouteNaming.Slug("  bridge -- ", "id") == "bridge");
        ExpectTrue("slug empty", RouteNaming.Slug("", "abcdef123456") == "route-123456");
        ExpectTrue("slug reserved", RouteNaming.Slug("CON", "id") == "_con");
        ExpectTrue("slug case", RouteNaming.Slug("Bridge", "id") == "bridge");
    }

    private static void TestUniqueStem()
    {
        ExpectTrue("stem free", RouteNaming.UniqueStem("bridge", "id", new[] { "harbour" }) == "bridge");
        ExpectTrue("stem suffix", RouteNaming.UniqueStem("bridge", "id", new[] { "bridge" }) == "bridge-2");
        ExpectTrue("stem next", RouteNaming.UniqueStem("bridge", "id", new[] { "bridge", "bridge-2" }) == "bridge-3");
    }

    private static void TestSaveLoad()
    {
        var dir = NewDir("save-load");
        var storage = new RouteStorage(dir);
        var route = Sample("Bridge", "aaaaaaaaaaaa");

        ExpectTrue("save returns true", storage.Save(route, out var saveError));
        ExpectTrue("save error empty", saveError.Length == 0);

        var path = storage.PathFor(route.Id);
        ExpectTrue("file exists", File.Exists(path));
        ExpectTrue("file named after route", Path.GetFileName(path) == "bridge.cfg");

        var loaded = new RouteStorage(dir).Load(4.0);
        ExpectTrue("one route loaded", loaded.Routes.Count == 1);
        if (loaded.Routes.Count == 1)
        {
            ExpectTrue("id survives", loaded.Routes[0].Id == route.Id);
            ExpectTrue("data survives", loaded.Routes[0].SameData(route));
        }
    }

    private static void TestSameName()
    {
        var dir = NewDir("same-name");
        var storage = new RouteStorage(dir);
        var first = Sample("Bridge", "aaaaaaaaaaa1");
        var second = Sample("Bridge", "aaaaaaaaaaa2");

        ExpectTrue("save first", storage.Save(first, out _));
        ExpectTrue("save second", storage.Save(second, out _));

        ExpectTrue("first name kept", Path.GetFileName(storage.PathFor(first.Id)) == "bridge.cfg");
        ExpectTrue("second suffixed", Path.GetFileName(storage.PathFor(second.Id)) == "bridge-2.cfg");
    }

    private static void TestRename()
    {
        var dir = NewDir("rename");
        var storage = new RouteStorage(dir);
        var route = Sample("Bridge", "aaaaaaaaaaa3");

        ExpectTrue("save before rename", storage.Save(route, out _));
        var oldPath = storage.PathFor(route.Id);

        route.Name = "Harbour";
        ExpectTrue("rename saves", storage.Save(route, out _));
        var newPath = storage.PathFor(route.Id);

        ExpectTrue("new file name", Path.GetFileName(newPath) == "harbour.cfg");
        ExpectTrue("old file removed", !File.Exists(oldPath));
    }

    private static void TestRenameConflict()
    {
        var dir = NewDir("rename-conflict");
        var storage = new RouteStorage(dir);
        var first = Sample("Bridge", "aaaaaaaaaaa5");
        var second = Sample("Harbour", "aaaaaaaaaaa6");

        ExpectTrue("conflict first save", storage.Save(first, out _));
        ExpectTrue("conflict second save", storage.Save(second, out _));

        second.Name = "Bridge";
        ExpectTrue("conflict rename save", storage.Save(second, out _));

        ExpectTrue("conflict suffix", Path.GetFileName(storage.PathFor(second.Id)) == "bridge-2.cfg");
        ExpectTrue("old conflict file removed", !File.Exists(Path.Combine(dir, "routes", "harbour.cfg")));
        ExpectTrue("first file untouched", Path.GetFileName(storage.PathFor(first.Id)) == "bridge.cfg");
    }

    private static void TestDelete()
    {
        var dir = NewDir("delete");
        var storage = new RouteStorage(dir);
        var route = Sample("Bridge", "aaaaaaaaaaa4");

        ExpectTrue("save before delete", storage.Save(route, out _));
        var path = storage.PathFor(route.Id);

        ExpectTrue("delete returns true", storage.Delete(route, out var error));
        ExpectTrue("delete error empty", error.Length == 0);
        ExpectTrue("file deleted", !File.Exists(path));

        var loaded = new RouteStorage(dir).Load(4.0);
        ExpectTrue("no routes after delete", loaded.Routes.Count == 0);
    }

    private static void TestMultiImport()
    {
        var dir = NewDir("multi");
        var routesDir = Path.Combine(dir, "routes");
        Directory.CreateDirectory(routesDir);

        var first = Sample("Multi one", "bbbbbbbbbbb1");
        var second = Sample("Multi two", "bbbbbbbbbbb2");
        WritePack(Path.Combine(routesDir, "pack.cfg"), first, second);

        var result = new RouteStorage(dir).Load(4.0);
        ExpectTrue("both imported", result.Routes.Count == 2);
        ExpectTrue("source archived", File.Exists(Path.Combine(routesDir, "pack.cfg.imported")));
        ExpectTrue("first per-route file", File.Exists(Path.Combine(routesDir, "multi-one.cfg")));
        ExpectTrue("second per-route file", File.Exists(Path.Combine(routesDir, "multi-two.cfg")));
    }

    private static void TestMigration()
    {
        var dir = NewDir("migration");
        WritePack(Path.Combine(dir, "routes.cfg"),
            Sample("Legacy one", "ccccccccccc1"),
            Sample("Legacy two", "ccccccccccc2"));

        var result = new RouteStorage(dir).Load(4.0);
        ExpectTrue("migration two routes", result.Routes.Count == 2);
        ExpectTrue("legacy file archived", Directory.GetFiles(dir, "routes.cfg.migrated-*").Length == 1);
        ExpectTrue("first migrated file", File.Exists(Path.Combine(dir, "routes", "legacy-one.cfg")));
        ExpectTrue("second migrated file", File.Exists(Path.Combine(dir, "routes", "legacy-two.cfg")));

        var reload = new RouteStorage(dir).Load(4.0);
        ExpectTrue("reload stays two", reload.Routes.Count == 2);
    }

    private static void TestDuplicateId()
    {
        var dir = NewDir("duplicate");
        var routesDir = Path.Combine(dir, "routes");
        Directory.CreateDirectory(routesDir);

        WritePack(Path.Combine(routesDir, "a.cfg"), Sample("One", "ddddddddddd1"));
        WritePack(Path.Combine(routesDir, "b.cfg"), Sample("Two", "ddddddddddd1"));

        var result = new RouteStorage(dir).Load(4.0);
        ExpectTrue("duplicate id loads one", result.Routes.Count == 1);
        ExpectTrue("duplicate noted", result.Notes.Count > 0);
    }

    private static void TestRunwayImport()
    {
        var dir = NewDir("runway");
        var routesDir = Path.Combine(dir, "routes");
        Directory.CreateDirectory(routesDir);

        var file = new ConfigNode("GlissadeRoutes");
        var runway = new ConfigNode("Runway");
        runway.AddValue("ident", "TEST_18");
        runway.AddValue("shortID", "T18");
        runway.AddValue("hdg", "180");
        runway.AddValue("body", "Kerbin");
        runway.AddValue("altMSL", "70");
        runway.AddValue("gsLatitude", "-0.05");
        runway.AddValue("gsLongitude", "285.3");
        file.AddNode(runway);

        if (!ConfigIo.Save(file, Path.Combine(routesDir, "nav.cfg"), true, out var error))
            throw new InvalidOperationException(error);

        var result = new RouteStorage(dir).Load(4.0);
        ExpectTrue("runway imported", result.Imported.Count == 1);
        ExpectTrue("no user route from runway", result.Routes.Count == 0);
        if (result.Imported.Count == 1)
        {
            ExpectTrue("runway id", result.Imported[0].Id.StartsWith("nav-"));
            ExpectTrue("runway read-only", result.Imported[0].BuiltIn);
        }
    }

    private static void WritePack(string path, params Route[] routes)
    {
        var root = new ConfigNode("GlissadeRoutes");
        root.AddValue("version", "1");
        foreach (var route in routes)
        {
            var node = new ConfigNode("Route");
            route.WriteTo(node);
            root.AddNode(node);
        }

        if (!ConfigIo.Save(root, path, true, out var error))
            throw new InvalidOperationException(error);
    }

    private static Route Sample(string name, string id)
    {
        return new Route
        {
            Id = id,
            Name = name,
            Body = "Kerbin",
            Lat = -0.05,
            Lon = 285.3,
            AltMsl = 70,
            HeadingDeg = 90,
            AngleDeg = 3,
            MinDistance = 250f,
            MaxDistance = 10000f,
            Spacing = 500f,
            Width = 40f,
            Height = 20f,
            EndWidth = 40f,
            EndHeight = 20f,
            TaperDistance = 0f
        };
    }

    private static string NewDir(string name)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void ExpectTrue(string name, bool condition)
    {
        if (condition) return;
        _failed++;
        Console.WriteLine("FAIL " + name);
    }

    private static string ResolveManaged()
    {
        var ksp = Environment.GetEnvironmentVariable("KSP_DIR");
        if (string.IsNullOrEmpty(ksp)) throw new InvalidOperationException("Set KSP_DIR to the game folder");
        return Path.Combine(ksp, "KSP_x64_Data", "Managed");
    }

    private static Assembly Resolve(object sender, ResolveEventArgs e)
    {
        var name = new AssemblyName(e.Name).Name + ".dll";
        var candidate = Path.Combine(Managed, name);
        return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
    }
}
