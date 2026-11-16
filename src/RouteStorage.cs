using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Glissade3D;

public sealed class StorageLoadResult
{
    public readonly List<Route> Routes = new List<Route>();
    public readonly List<Route> Imported = new List<Route>();
    public readonly List<string> Notes = new List<string>();
}

/// <summary>
/// One route per file under PluginData/Glissade3D/routes. The old single-file
/// routes.cfg is migrated once and then left as evidence. Multi-route files are
/// imported and archived, so a shared pack can be dropped into the folder.
/// </summary>
public sealed class RouteStorage
{
    private readonly string _routesDir;
    private readonly string _legacyPath;
    private readonly Dictionary<string, string> _paths = new Dictionary<string, string>(StringComparer.Ordinal);

    public RouteStorage(string pluginDataDir)
    {
        _routesDir = Path.Combine(pluginDataDir, "routes");
        _legacyPath = Path.Combine(pluginDataDir, "routes.cfg");
    }

    public string RoutesDirectory => _routesDir;

    public string PathFor(string id)
    {
        return !string.IsNullOrEmpty(id) && _paths.TryGetValue(id, out var path) ? path : "";
    }

    public StorageLoadResult Load(double defaultAngle)
    {
        _paths.Clear();
        var result = new StorageLoadResult();
        MigrateLegacy(defaultAngle, result);
        Scan(defaultAngle, result);
        return result;
    }

    public bool Save(Route route, out string error)
    {
        return WriteRoute(route, out error);
    }

    public bool Delete(Route route, out string error)
    {
        error = "";
        var path = PathFor(route.Id);
        if (string.IsNullOrEmpty(path))
        {
            _paths.Remove(route.Id);
            return true;
        }

        try
        {
            if (File.Exists(path)) File.Delete(path);
            _paths.Remove(route.Id);
            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }

    private void MigrateLegacy(double defaultAngle, StorageLoadResult result)
    {
        if (!File.Exists(_legacyPath)) return;

        if (HasRouteFiles())
        {
            result.Notes.Add("legacy routes.cfg ignored: routes folder already has files");
            return;
        }

        var file = ConfigIo.Load(_legacyPath);
        if (file == null)
        {
            result.Notes.Add("legacy routes.cfg unreadable");
            return;
        }

        var written = new List<string>();
        foreach (var node in file.GetNodes("Route"))
        {
            var route = Route.ReadFrom(node);
            if (route == null)
            {
                result.Notes.Add("legacy routes.cfg has an invalid route");
                continue;
            }

            route.BuiltIn = false;
            route.AngleFromDefault = false;
            if (!WriteRoute(route, out var error))
            {
                result.Notes.Add("migration failed: " + error);
                foreach (var path in written) TryDelete(path);
                return;
            }

            written.Add(PathFor(route.Id));
        }

        var migrated = _legacyPath + ".migrated-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        try
        {
            File.Move(_legacyPath, migrated);
            result.Notes.Add($"migrated {written.Count} route(s); old file is {Path.GetFileName(migrated)}");
        }
        catch (Exception e)
        {
            result.Notes.Add("could not archive routes.cfg: " + e.Message);
        }
    }

    private void Scan(double defaultAngle, StorageLoadResult result)
    {
        if (!Directory.Exists(_routesDir)) return;

        var files = Directory.GetFiles(_routesDir, "*.cfg", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var multi = new List<KeyValuePair<string, List<Route>>>();

        foreach (var path in files)
        {
            var file = ConfigIo.Load(path);
            if (file == null)
            {
                result.Notes.Add("unreadable: " + Relative(path));
                continue;
            }

            var parsed = new List<Route>();
            foreach (var node in file.GetNodes("Route"))
            {
                var route = Route.ReadFrom(node);
                if (route == null)
                {
                    result.Notes.Add("invalid route in " + Relative(path));
                    continue;
                }

                route.BuiltIn = false;
                route.AngleFromDefault = false;
                if (!seen.Add(route.Id))
                {
                    result.Notes.Add($"duplicate id {route.Id} in {Relative(path)}");
                    continue;
                }

                parsed.Add(route);
            }

            foreach (var node in file.GetNodes())
            {
                if (!IsRunwayNode(node.name)) continue;
                if (!RunwayInfo.TryParse(node, out var runway)) continue;
                var imported = RouteStore.FromRunway(runway, defaultAngle);
                if (imported != null) result.Imported.Add(imported);
            }

            if (parsed.Count == 1)
            {
                result.Routes.Add(parsed[0]);
                _paths[parsed[0].Id] = path;
            }
            else if (parsed.Count > 1)
            {
                multi.Add(new KeyValuePair<string, List<Route>>(path, parsed));
            }
            else if (file.GetNodes("Route").Length == 0
                     && file.GetNodes("Runway").Length == 0
                     && file.GetNodes("NavUtilRunway").Length == 0)
            {
                result.Notes.Add("no routes in " + Relative(path));
            }
        }

        foreach (var pair in multi)
        {
            var written = true;
            foreach (var route in pair.Value)
            {
                if (WriteRoute(route, out var error)) continue;
                result.Notes.Add("import failed: " + error);
                written = false;
                break;
            }

            if (written)
            {
                try
                {
                    File.Move(pair.Key, pair.Key + ".imported");
                    result.Notes.Add("imported multi-route file " + Relative(pair.Key));
                }
                catch (Exception e)
                {
                    result.Notes.Add("could not archive " + Relative(pair.Key) + ": " + e.Message);
                }
            }

            result.Routes.AddRange(pair.Value);
        }
    }

    private bool WriteRoute(Route route, out string error)
    {
        error = "";
        if (route == null)
        {
            error = "route is null";
            return false;
        }

        try
        {
            Directory.CreateDirectory(_routesDir);
            var current = PathFor(route.Id);
            var target = ResolvePath(route, current);

            var root = new ConfigNode("GlissadeRoutes");
            root.AddValue("version", Route.FormatVersion.ToString(CultureInfo.InvariantCulture));
            var node = new ConfigNode("Route");
            route.WriteTo(node);
            root.AddNode(node);

            if (!ConfigIo.Save(root, target, false, out error)) return false;
            _paths[route.Id] = target;

            if (!string.IsNullOrEmpty(current)
                && !string.Equals(current, target, StringComparison.OrdinalIgnoreCase)
                && File.Exists(current))
            {
                try
                {
                    File.Delete(current);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Glissade3D] could not remove old route file {current}: {e.Message}");
                }
            }

            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }

    private string ResolvePath(Route route, string current)
    {
        var desired = RouteNaming.Slug(route.Name, route.Id);

        if (!string.IsNullOrEmpty(current) && File.Exists(current))
        {
            var currentStem = Path.GetFileNameWithoutExtension(current);
            if (string.Equals(currentStem, desired, StringComparison.OrdinalIgnoreCase)) return current;

            var folder = Path.GetDirectoryName(current) ?? _routesDir;
            var stem = RouteNaming.UniqueStem(desired, route.Id, TakenStems(route.Id));
            return Path.Combine(folder, stem + ".cfg");
        }

        var newStem = RouteNaming.UniqueStem(desired, route.Id, TakenStems(route.Id));
        return Path.Combine(_routesDir, newStem + ".cfg");
    }

    private HashSet<string> TakenStems(string exceptId)
    {
        var stems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(_routesDir)) return stems;

        var own = PathFor(exceptId);
        foreach (var path in Directory.GetFiles(_routesDir, "*.cfg", SearchOption.AllDirectories))
        {
            if (!string.IsNullOrEmpty(own) && string.Equals(path, own, StringComparison.OrdinalIgnoreCase)) continue;
            stems.Add(Path.GetFileNameWithoutExtension(path));
        }
        return stems;
    }

    private bool HasRouteFiles()
    {
        return Directory.Exists(_routesDir)
               && Directory.EnumerateFiles(_routesDir, "*.cfg", SearchOption.AllDirectories).Any();
    }

    private string Relative(string path)
    {
        if (string.IsNullOrEmpty(_routesDir)) return path;
        return path.StartsWith(_routesDir, StringComparison.OrdinalIgnoreCase)
            ? path.Substring(_routesDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : path;
    }

    private static bool IsRunwayNode(string name)
    {
        return name == "Runway" || name == "NavUtilRunway";
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path);
        }
        catch (Exception)
        {
            // Best effort: the migration report already carries the error.
        }
    }
}
