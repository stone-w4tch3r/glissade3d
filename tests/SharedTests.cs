using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using Glissade3D;

static class SharedTests
{
    private static int _failed;
    private static readonly string Managed = ResolveManaged();
    private static string _root;
    private static string _sharedDir = "";

    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        if (args.Length > 0) _sharedDir = args[0];

        _root = Path.Combine(Path.GetTempPath(), "glissade-shared-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        try
        {
            Run("catalog traversal", TestTraversal);
            Run("catalog wrapperless", TestWrapperlessManifest);
            Run("base url", TestBaseUrl);
            Run("live install", TestLiveInstall);
            Run("live reinstall", TestReinstall);
            Run("live update", TestUpdate);
            Run("modified kept", TestModifiedKept);
            Run("id conflict", TestConflict);
            Run("bad hash", TestBadHash);
            Run("missing file", TestMissingFile);
            Run("uninstall", TestUninstall);
            Run("live 404", TestLive404);
            if (!string.IsNullOrEmpty(_sharedDir)) Run("real shared over http", TestRealShared);
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

        Console.WriteLine(_failed == 0 ? "OK: all shared checks passed" : $"FAILED: {_failed}");
        return _failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
        }
        catch (Exception e)
        {
            _failed++;
            Console.WriteLine($"FAIL {name}: {e.GetType().Name}: {e.Message}");
        }
    }

    private static void TestTraversal()
    {
        var text = ManifestText(new[]
        {
            new Entry("id1", "name", "../evil.cfg", new string('0', 64))
        });
        Expect(SharedCatalog.Parse(text, out var error) == null, "traversal rejected");
        Expect(!string.IsNullOrEmpty(error), "traversal has error");
    }

    private static void TestWrapperlessManifest()
    {
        var text = "version = 1\nEntry\n{\n id = a1\n name = n1\n file = routes/a.cfg\n sha256 = " + new string('a', 64) + "\n}\n";
        var catalog = SharedCatalog.Parse(text, out var error);
        Expect(catalog != null, "wrapperless parsed: " + error);
        Expect(catalog != null && catalog.Entries.Count == 1, "wrapperless entry count");
    }

    private static void TestBaseUrl()
    {
        var url = "http://127.0.0.1:8765/";
        var encoded = SharedInstaller.EncodeBaseUrl(url);
        Expect(!encoded.Contains("//"), "encoded url has no comment marker");
        Expect(SharedInstaller.DecodeBaseUrl(encoded) == url, "url round-trip");
        Expect(SharedInstaller.NormalizeBaseUrl("http:") == "", "empty host rejected");
        Expect(SharedInstaller.NormalizeBaseUrl("https://example.com/x") == "https://example.com/x/", "slash added");
    }

    private static void TestLiveInstall()
    {
        var web = BuildWeb("install", out _, out _);
        using (var server = new LiveServer(web))
        {
            var dir = NewDir("install-data");
            var storage = new RouteStorage(dir);
            var state = new SharedInstallState();
            var statePath = Path.Combine(dir, "shared-installed.cfg");
            var routes = new List<Route>();

            var result = InstallFromServer(server, storage, routes, state, statePath);
            Expect(result.Installed == 2, "installed two");
            Expect(result.Failed == 0, "no failures");

            var loaded = storage.Load(3.0);
            Expect(loaded.Routes.Count == 2, "two files on disk");
            Expect(File.Exists(statePath), "state written");
            Expect(SharedInstallState.Load(statePath).Count == 2, "state has two");
        }
    }

    private static void TestReinstall()
    {
        var web = BuildWeb("reinstall", out _, out _);
        using (var server = new LiveServer(web))
        {
            var dir = NewDir("reinstall-data");
            var storage = new RouteStorage(dir);
            var state = new SharedInstallState();
            var statePath = Path.Combine(dir, "shared-installed.cfg");

            var first = InstallFromServer(server, storage, new List<Route>(), state, statePath);
            Expect(first.Installed == 2, "first install");

            var routes = storage.Load(3.0).Routes;
            var again = InstallFromServer(server, storage, routes, SharedInstallState.Load(statePath), statePath);
            Expect(again.UpToDate == 2, "everything up to date");
            Expect(again.Installed == 0 && again.Updated == 0, "no changes");
        }
    }

    private static void TestUpdate()
    {
        var web = BuildWeb("update", out var one, out var two);
        using (var server = new LiveServer(web))
        {
            var dir = NewDir("update-data");
            var storage = new RouteStorage(dir);
            var statePath = Path.Combine(dir, "shared-installed.cfg");

            var first = InstallFromServer(server, storage, new List<Route>(), new SharedInstallState(), statePath);
            Expect(first.Installed == 2, "update base install");

            one.HeadingDeg = 45;
            WriteRouteFile(Path.Combine(web, "routes", "one.cfg"), one);
            WriteManifest(web, new[] { EntryFor(web, one, "routes/one.cfg"), EntryFor(web, two, "routes/two.cfg") });

            var routes = storage.Load(3.0).Routes;
            var result = InstallFromServer(server, storage, routes, SharedInstallState.Load(statePath), statePath);
            Expect(result.Updated == 1, "one updated");
            Expect(result.UpToDate == 1, "one up to date");

            var loaded = storage.Load(3.0).Routes;
            var updated = Find(loaded, one.Id);
            Expect(updated != null && Math.Abs(updated.HeadingDeg - 45) < 1e-6, "new heading saved");
        }
    }

    private static void TestModifiedKept()
    {
        var web = BuildWeb("modified", out var one, out var two);
        using (var server = new LiveServer(web))
        {
            var dir = NewDir("modified-data");
            var storage = new RouteStorage(dir);
            var statePath = Path.Combine(dir, "shared-installed.cfg");

            InstallFromServer(server, storage, new List<Route>(), new SharedInstallState(), statePath);

            var routes = storage.Load(3.0).Routes;
            var local = Find(routes, one.Id);
            local.Name = "my edit";
            Expect(storage.Save(local, out var editError), "local edit saved: " + editError);
            routes = storage.Load(3.0).Routes;

            one.HeadingDeg = 60;
            WriteRouteFile(Path.Combine(web, "routes", "one.cfg"), one);
            WriteManifest(web, new[] { EntryFor(web, one, "routes/one.cfg"), EntryFor(web, two, "routes/two.cfg") });

            var result = InstallFromServer(server, storage, routes, SharedInstallState.Load(statePath), statePath);
            Expect(result.SkippedModified == 1, "modified skipped");
            Expect(result.Updated == 0, "modified not updated");
        }
    }

    private static void TestConflict()
    {
        var web = BuildWeb("conflict", out var one, out var two);
        using (var server = new LiveServer(web))
        {
            var dir = NewDir("conflict-data");
            var storage = new RouteStorage(dir);
            var statePath = Path.Combine(dir, "shared-installed.cfg");

            var user = Sample("my own", one.Id);
            Expect(storage.Save(user, out var error), "user route saved: " + error);
            var routes = storage.Load(3.0).Routes;

            var result = InstallFromServer(server, storage, routes, new SharedInstallState(), statePath);
            Expect(result.SkippedConflict == 1, "id conflict skipped");
            Expect(result.Installed == 1, "other route installed");
        }
    }

    private static void TestBadHash()
    {
        var web = BuildWeb("bad-hash", out var one, out var two);
        using (var server = new LiveServer(web))
        {
            var dir = NewDir("bad-hash-data");
            var storage = new RouteStorage(dir);
            var statePath = Path.Combine(dir, "shared-installed.cfg");

            WriteManifest(web, new[]
            {
                new Entry(one.Id, one.Name, "routes/one.cfg", new string('0', 64)),
                EntryFor(web, two, "routes/two.cfg")
            });

            var result = InstallFromServer(server, storage, new List<Route>(), new SharedInstallState(), statePath);
            Expect(result.Failed == 1, "bad hash failed");
            Expect(result.Installed == 1, "good file installed");
        }
    }

    private static void TestMissingFile()
    {
        var web = BuildWeb("missing", out var one, out _);
        using (var server = new LiveServer(web))
        {
            var dir = NewDir("missing-data");
            var storage = new RouteStorage(dir);
            var statePath = Path.Combine(dir, "shared-installed.cfg");

            WriteManifest(web, new[]
            {
                new Entry(one.Id, one.Name, "routes/missing.cfg", new string('a', 64))
            });

            var failed = false;
            try
            {
                InstallFromServer(server, storage, new List<Route>(), new SharedInstallState(), statePath);
            }
            catch (WebException)
            {
                failed = true;
            }
            Expect(failed, "missing file raises 404");
        }
    }

    private static void TestUninstall()
    {
        var web = BuildWeb("uninstall", out var one, out var two);
        using (var server = new LiveServer(web))
        {
            var dir = NewDir("uninstall-data");
            var storage = new RouteStorage(dir);
            var statePath = Path.Combine(dir, "shared-installed.cfg");

            InstallFromServer(server, storage, new List<Route>(), new SharedInstallState(), statePath);

            var routes = storage.Load(3.0).Routes;
            var local = Find(routes, one.Id);
            local.Name = "edited shared";
            Expect(storage.Save(local, out var editError), "edit before uninstall: " + editError);

            routes = storage.Load(3.0).Routes;
            var state = SharedInstallState.Load(statePath);
            var result = SharedInstaller.Uninstall(storage, routes, state, statePath);

            Expect(result.Removed == 1, "one removed");
            Expect(result.KeptModified == 1, "one kept");
            Expect(state.Count == 0, "state cleared");

            var remaining = storage.Load(3.0).Routes;
            Expect(Find(remaining, one.Id) != null, "edited route remains");
            Expect(Find(remaining, two.Id) == null, "untouched route removed");
            Expect(SharedInstallState.Load(statePath).Count == 0, "state file cleared");
        }
    }

    private static void TestLive404()
    {
        var web = BuildWeb("404", out _, out _);
        using (var server = new LiveServer(web))
        {
            var failed = false;
            try
            {
                Fetch(server.Url("nope.cfg"));
            }
            catch (WebException e)
            {
                failed = ((HttpWebResponse)e.Response).StatusCode == HttpStatusCode.NotFound;
            }
            Expect(failed, "404 over live server");
        }
    }

    private static void TestRealShared()
    {
        var dir = NewDir("real-shared-data");
        var storage = new RouteStorage(dir);
        var statePath = Path.Combine(dir, "shared-installed.cfg");
        using (var server = new LiveServer(_sharedDir))
        {
            var result = InstallFromServer(server, storage, new List<Route>(), new SharedInstallState(), statePath);
            Expect(result.Failed == 0, "real manifest has no failures");
            Expect(result.Installed >= 1, "real manifest installed something");
            Expect(storage.Load(3.0).Routes.Count == result.Installed, "real files on disk");
        }
    }

    private static SharedInstallResult InstallFromServer(LiveServer server, RouteStorage storage,
        List<Route> routes, SharedInstallState state, string statePath)
    {
        var manifestBytes = Fetch(server.Url(SharedInstaller.ManifestName));
        var catalog = SharedCatalog.Parse(Encoding.UTF8.GetString(manifestBytes), out var error);
        if (catalog == null) throw new InvalidOperationException(error);

        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in catalog.Entries)
        {
            files[entry.File] = Fetch(server.Url(entry.File));
        }
        return SharedInstaller.Install(catalog, files, storage, routes, state, statePath);
    }

    private static string BuildWeb(string name, out Route one, out Route two)
    {
        var web = NewDir("web-" + name);
        Directory.CreateDirectory(Path.Combine(web, "routes"));
        one = Sample("web one", "fffffffffff1");
        two = Sample("web two", "fffffffffff2");
        WriteRouteFile(Path.Combine(web, "routes", "one.cfg"), one);
        WriteRouteFile(Path.Combine(web, "routes", "two.cfg"), two);
        WriteManifest(web, new[]
        {
            EntryFor(web, one, "routes/one.cfg"),
            EntryFor(web, two, "routes/two.cfg")
        });
        return web;
    }

    private static Entry EntryFor(string web, Route route, string relative)
    {
        var bytes = File.ReadAllBytes(Path.Combine(web, relative.Replace('/', Path.DirectorySeparatorChar)));
        return new Entry(route.Id, route.Name, relative, SharedHash.Bytes(bytes));
    }

    private static void WriteRouteFile(string path, Route route)
    {
        var root = new ConfigNode("GlissadeRoutes");
        var node = new ConfigNode("Route");
        route.WriteTo(node);
        root.AddNode(node);
        if (!ConfigIo.Save(root, path, true, out var error))
            throw new InvalidOperationException(error);
    }

    private static void WriteManifest(string web, IEnumerable<Entry> entries)
    {
        var text = ManifestText(entries);
        File.WriteAllText(Path.Combine(web, "index.cfg"), text, new UTF8Encoding(false));
    }

    private static string ManifestText(IEnumerable<Entry> entries)
    {
        var builder = new StringBuilder();
        builder.AppendLine("SharedRoutes");
        builder.AppendLine("{");
        builder.AppendLine("    version = 1");
        foreach (var entry in entries)
        {
            builder.AppendLine("    Entry");
            builder.AppendLine("    {");
            builder.AppendLine("        id = " + entry.Id);
            builder.AppendLine("        name = " + entry.Name);
            builder.AppendLine("        file = " + entry.File);
            builder.AppendLine("        sha256 = " + entry.Hash);
            builder.AppendLine("    }");
        }
        builder.AppendLine("}");
        return builder.ToString();
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
            MaxDistance = 5000f,
            Spacing = 500f,
            Width = 40f,
            Height = 20f,
            EndWidth = 20f,
            EndHeight = 10f,
            TaperDistance = 400f
        };
    }

    private static Route Find(IList<Route> routes, string id)
    {
        foreach (var route in routes) if (route.Id == id) return route;
        return null;
    }

    private static string NewDir(string name)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static byte[] Fetch(string url)
    {
        var request = (HttpWebRequest)WebRequest.Create(url);
        request.Timeout = 10000;
        request.UserAgent = "Glissade3D-tests";
        using (var response = (HttpWebResponse)request.GetResponse())
        using (var stream = response.GetResponseStream())
        using (var memory = new MemoryStream())
        {
            stream.CopyTo(memory);
            return memory.ToArray();
        }
    }

    private static void Expect(bool condition, string message)
    {
        if (condition) return;
        _failed++;
        Console.WriteLine("FAIL " + message);
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

    private struct Entry
    {
        public readonly string Id;
        public readonly string Name;
        public readonly string File;
        public readonly string Hash;

        public Entry(string id, string name, string file, string hash)
        {
            Id = id;
            Name = name;
            File = file;
            Hash = hash;
        }
    }

    private sealed class LiveServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly string _root;

        public int Port { get; }

        public LiveServer(string root)
        {
            _root = Path.GetFullPath(root);
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            var thread = new Thread(Loop) { IsBackground = true };
            thread.Start();
        }

        public string Url(string relative) => $"http://127.0.0.1:{Port}/{relative.TrimStart('/')}";

        private void Loop()
        {
            while (true)
            {
                TcpClient client;
                try { client = _listener.AcceptTcpClient(); }
                catch { return; }
                ThreadPool.QueueUserWorkItem(_ => Handle(client));
            }
        }

        private void Handle(TcpClient client)
        {
            try
            {
                using (client)
                using (var stream = client.GetStream())
                {
                    var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                    var requestLine = reader.ReadLine();
                    if (string.IsNullOrEmpty(requestLine)) return;
                    string header;
                    while (!string.IsNullOrEmpty(header = reader.ReadLine())) { }

                    var parts = requestLine.Split(' ');
                    if (parts.Length < 2) return;
                    var relative = Uri.UnescapeDataString(parts[1].Split('?')[0]).TrimStart('/');
                    var full = Path.GetFullPath(Path.Combine(_root, relative));

                    if (!full.StartsWith(_root, StringComparison.Ordinal) || !File.Exists(full))
                    {
                        Write(stream, "404 Not Found", Encoding.ASCII.GetBytes("not found"));
                        return;
                    }

                    Write(stream, "200 OK", File.ReadAllBytes(full));
                }
            }
            catch (Exception)
            {
                // A test server: a broken connection must not kill the loop.
            }
        }

        private static void Write(Stream stream, string status, byte[] body)
        {
            var header = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {status}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
            stream.Write(header, 0, header.Length);
            stream.Write(body, 0, body.Length);
        }

        public void Dispose()
        {
            try { _listener.Stop(); } catch { }
        }
    }
}
