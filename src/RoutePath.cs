using System;
using System.Collections.Generic;
using UnityEngine;

namespace Glissade3D;

/// <summary>
/// Builds the flight path for a route from a celestial body, and adds the
/// terrain-aware warnings that pure validation cannot produce.
/// </summary>
public static class RoutePath
{
    public static ApproachPath Build(Route route, CelestialBody body, double defaultAngle)
    {
        if (route == null || body == null) return null;

        var origin = body.GetWorldSurfacePosition(route.Lat, route.Lon, route.AltMsl);
        var ahead = OffsetLatLon(body, route.Lat, route.Lon, route.HeadingDeg, 1000.0);
        var aheadPosition = body.GetWorldSurfacePosition(ahead.lat, ahead.lon, route.AltMsl);

        var heading = aheadPosition - origin;
        var up = origin - body.position;
        if (heading.magnitude < 1.0 || up.magnitude < 1.0) return null;

        return new ApproachPath(ToVec(origin), ToVec(heading), ToVec(up), route.EffectiveAngle(defaultAngle));
    }

    public static Vec VecFrom(Vector3d value) => new Vec(value.x, value.y, value.z);

    public static (double lat, double lon) OffsetLatLon(CelestialBody body, double lat, double lon, double headingDeg, double distance)
    {
        var rad = headingDeg * Math.PI / 180.0;
        var latRad = lat * Math.PI / 180.0;
        var cosLat = Math.Max(Math.Cos(latRad), 1e-6);
        var deltaLat = distance * Math.Cos(rad) / body.Radius * 180.0 / Math.PI;
        var deltaLon = distance * Math.Sin(rad) / (body.Radius * cosLat) * 180.0 / Math.PI;
        return (lat + deltaLat, NormalizeLon(lon + deltaLon));
    }

    public static double NormalizeLon(double lon) => RouteMath.NormalizeLon(lon);

    /// <summary>
    /// Terrain-only warnings. Static structures such as the bridge are not
    /// sampled and are never claimed to be checked.
    /// </summary>
    public static void AddTerrainWarnings(Route route, CelestialBody body, double defaultAngle, List<RouteProblem> problems)
    {
        if (route == null || body == null || problems == null) return;
        if (!RouteMath.IsFiniteRoute(route)) return;

        var path = Build(route, body, defaultAngle);
        if (path == null) return;

        var count = RouteMath.GateCount(route);
        var clipped = false;
        var ground = body.TerrainAltitude(route.Lat, route.Lon, true);
        if (route.AltMsl < ground - 1.0) problems.Add(new RouteProblem(false, "warn_anchor_underground"));

        for (var i = 0; i < count; i++)
        {
            var slant = RouteMath.SlantOf(route, i);
            var frame = path.GateAt(slant);
            RouteMath.SizeAt(route, slant, out _, out var height);

            var world = new Vector3d(frame.Center.X, frame.Center.Y, frame.Center.Z);
            var bottom = world - new Vector3d(frame.Up.X, frame.Up.Y, frame.Up.Z) * (height * 0.5);
            var lat = body.GetLatitude(bottom);
            var lon = body.GetLongitude(bottom);
            if (body.GetAltitude(bottom) < body.TerrainAltitude(lat, lon, true) - 0.5)
            {
                clipped = true;
                break;
            }
        }

        if (clipped) problems.Add(new RouteProblem(false, "warn_terrain"));
    }

    public static Vec ToVec(Vector3d value) => new Vec(value.x, value.y, value.z);

    public static Vector3d ToVector3d(Vec value) => new Vector3d(value.X, value.Y, value.Z);
}
