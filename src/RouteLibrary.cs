using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Glissade3D;

/// <summary>
/// The live route list: imported NavUtilities runways, optional legacy entries
/// from customRunways.cfg, and our own routes from PluginData/Glissade3D/routes.
/// Only user routes are ever written back.
/// </summary>
public static class RouteLibrary
{
    public static readonly List<Route> Routes = new List<Route>();

    public static string LastError { get; private set; } = "";

    private static bool _loaded;
    private static RouteStorage _storage;

    private static string PluginDir => Path.Combine(KSPUtil.ApplicationRootPath, "PluginData", "Glissade3D");
    private static string LegacyPath => Path.Combine(KSPUtil.ApplicationRootPath, "PluginData", "customRunways.cfg");

    public static string RoutesDirectory => Storage.RoutesDirectory;

    private static RouteStorage Storage => _storage ??= new RouteStorage(PluginDir);

    public static void EnsureLoaded(double defaultAngle)
    {
        if (_loaded) return;
        _loaded = true;
        Reload(defaultAngle);
    }

    public static void Reload(double defaultAngle)
    {
        Routes.Clear();
        LastError = "";

        var imported = new List<Route>();
        var importedIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var route in LoadGameDatabaseRoutes(defaultAngle))
        {
            if (importedIds.Add(route.Id)) imported.Add(route);
        }
        foreach (var route in LoadLegacyRoutes(defaultAngle))
        {
            if (importedIds.Add(route.Id)) imported.Add(route);
        }

        var result = Storage.Load(defaultAngle);
        foreach (var note in result.Notes) Debug.Log("[Glissade3D] routes: " + note);
        foreach (var route in result.Imported)
        {
            if (importedIds.Add(route.Id)) imported.Add(route);
        }

        Routes.AddRange(RouteStore.Merge(imported, result.Routes));

        Debug.Log($"[Glissade3D] routes: {Routes.Count} ({imported.Count} import, {result.Routes.Count} user)");
        foreach (var route in Routes)
        {
            Debug.Log($"[Glissade3D]   {route.Body} {route.DisplayName} id={route.Id} hdg={route.HeadingDeg:0.#} angle={route.AngleDeg:0.#} builtin={route.BuiltIn}");
        }
    }

    public static List<Route> ForBody(string body) => RouteStore.ForBody(Routes, body);

    public static Route ById(string id) => RouteStore.Find(Routes, id);

    public static bool IsNewId(string id) => RouteStore.IndexOfId(Routes, id) < 0;

    public static string PathFor(string id) => Storage.PathFor(id);

    /// <summary>Adds or replaces one of our own routes and writes its file.</summary>
    public static bool UpsertUser(Route route, out string error)
    {
        if (route == null)
        {
            error = "route is null";
            return false;
        }

        route.BuiltIn = false;
        route.AngleFromDefault = false;

        if (!Storage.Save(route, out error))
        {
            LastError = error;
            return false;
        }

        var index = RouteStore.IndexOfId(Routes, route.Id);
        if (index >= 0) Routes[index] = route;
        else Routes.Add(route);
        return true;
    }

    public static bool DeleteUser(string id, out string error)
    {
        var index = RouteStore.IndexOfId(Routes, id);
        if (index < 0)
        {
            error = "route not found";
            return false;
        }

        var route = Routes[index];
        if (route.BuiltIn)
        {
            error = "read-only route";
            return false;
        }

        if (!Storage.Delete(route, out error))
        {
            LastError = error;
            return false;
        }

        Routes.RemoveAt(index);
        return true;
    }

    private static List<Route> LoadGameDatabaseRoutes(double defaultAngle)
    {
        var result = new List<Route>();
        ConfigNode[] nodes;
        try
        {
            nodes = GameDatabase.Instance.GetConfigNodes("Runway");
        }
        catch (Exception e)
        {
            Debug.LogError($"[Glissade3D] runway lookup failed: {e.Message}");
            return result;
        }

        if (nodes == null) return result;
        foreach (var node in nodes)
        {
            if (!RunwayInfo.TryParse(node, out var runway)) continue;
            var route = RouteStore.FromRunway(runway, defaultAngle);
            if (route != null) result.Add(route);
        }
        return result;
    }

    private static List<Route> LoadLegacyRoutes(double defaultAngle)
    {
        var result = new List<Route>();
        if (!File.Exists(LegacyPath)) return result;

        try
        {
            var file = ConfigIo.Load(LegacyPath);
            if (file == null) return result;

            foreach (var reader in new[] { "Runway", "NavUtilRunway" })
            {
                foreach (var node in file.GetNodes(reader))
                {
                    if (!RunwayInfo.TryParse(node, out var runway)) continue;
                    var route = RouteStore.FromRunway(runway, defaultAngle);
                    if (route != null) result.Add(route);
                }
            }
            Debug.Log($"[Glissade3D] legacy runways imported: {result.Count}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[Glissade3D] legacy runway file failed: {e.Message}");
        }
        return result;
    }
}
