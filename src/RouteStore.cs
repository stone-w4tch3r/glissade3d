using System;
using System.Collections.Generic;

namespace Glissade3D;

/// <summary>
/// Pure library operations: merging imported and user routes, duplicating,
/// names. No Unity, no file access — so it is all unit-testable.
/// </summary>
public static class RouteStore
{
    /// <summary>
    /// Built-ins come first in their original order. A user route with the same
    /// id replaces the built-in in place, so the list order stays stable.
    /// </summary>
    public static List<Route> Merge(IEnumerable<Route> builtIns, IEnumerable<Route> user)
    {
        var result = new List<Route>();
        if (builtIns != null)
        {
            foreach (var route in builtIns)
            {
                if (route == null || string.IsNullOrEmpty(route.Id)) continue;
                result.Add(route);
            }
        }

        if (user == null) return result;

        foreach (var route in user)
        {
            if (route == null || string.IsNullOrEmpty(route.Id)) continue;
            var index = IndexOfId(result, route.Id);
            if (index >= 0)
            {
                route.BuiltIn = false;
                result[index] = route;
            }
            else
            {
                route.BuiltIn = false;
                result.Add(route);
            }
        }

        return result;
    }

    public static int IndexOfId(List<Route> routes, string id)
    {
        if (routes == null || string.IsNullOrEmpty(id)) return -1;
        for (var i = 0; i < routes.Count; i++)
        {
            if (string.Equals(routes[i].Id, id, StringComparison.Ordinal)) return i;
        }
        return -1;
    }

    public static Route Find(List<Route> routes, string id)
    {
        var index = IndexOfId(routes, id);
        return index >= 0 ? routes[index] : null;
    }

    public static List<Route> ForBody(IEnumerable<Route> routes, string body)
    {
        var result = new List<Route>();
        foreach (var route in routes)
        {
            if (string.Equals(route.Body, body, StringComparison.OrdinalIgnoreCase)) result.Add(route);
        }
        return result;
    }

    /// <summary>A copy with a new id, a unique name and no read-only flag.</summary>
    public static Route Duplicate(Route source, IEnumerable<Route> existing)
    {
        if (source == null) return null;
        var copy = source.Clone();
        copy.Id = Route.NewId();
        copy.Name = UniqueName(SourceName(source) + " copy", existing);
        copy.BuiltIn = false;
        copy.AngleFromDefault = false;
        copy.AngleDeg = source.AngleDeg;
        return copy;
    }

    public static string UniqueName(string wanted, IEnumerable<Route> existing)
    {
        var name = string.IsNullOrEmpty(wanted) ? "Route" : wanted.Trim();
        if (existing == null) return name;

        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var route in existing) taken.Add(route.DisplayName);

        if (!taken.Contains(name)) return name;
        for (var n = 2; n < 1000; n++)
        {
            var candidate = $"{name} ({n})";
            if (!taken.Contains(candidate)) return candidate;
        }
        return name + " " + Route.NewId();
    }

    /// <summary>
    /// Converts a NavUtilities runway node into a route. Built-in routes follow
    /// the global default angle, so the slider remains their reset-able override.
    /// </summary>
    public static Route FromRunway(RunwayInfo runway, double defaultAngle)
    {
        if (runway == null) return null;

        var route = new Route
        {
            Id = BuiltInId(runway),
            Name = runway.DisplayName,
            Body = runway.Body,
            Lat = runway.GsLat,
            Lon = runway.GsLon,
            AltMsl = runway.AltMsl,
            HeadingDeg = runway.HeadingDeg,
            AngleDeg = defaultAngle,
            BuiltIn = true,
            AngleFromDefault = true
        };
        return route;
    }

    public static string BuiltInId(RunwayInfo runway)
    {
        var heading = Math.Round(runway.HeadingDeg, 1).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        return $"nav-{runway.Body}-{runway.DisplayName}-{heading}".ToLowerInvariant().Replace(' ', '_');
    }

    private static string SourceName(Route route)
    {
        if (!string.IsNullOrEmpty(route.Name)) return route.Name;
        return string.IsNullOrEmpty(route.Id) ? "Route" : route.Id;
    }
}
