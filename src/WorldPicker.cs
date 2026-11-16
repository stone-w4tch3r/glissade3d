using UnityEngine;

namespace Glissade3D;

/// <summary>
/// Screen to world picking and the two drag maths: a point on a tangent plane
/// (place) and the closest point on the vertical axis (altitude).
/// </summary>
public static class WorldPicker
{
    public static Ray ScreenRay(Camera camera, Vector2 screen)
    {
        return camera.ScreenPointToRay(new Vector3(screen.x, screen.y, 0f));
    }

    /// <summary>
    /// Picks the world point under the cursor. Hits terrain and static buildings;
    /// hits on vessels are skipped, because a route point is never on a craft.
    /// With nothing under the cursor, returns the ray point closest to the
    /// fallback, which is what makes clicking "towards the marker" predictable.
    /// </summary>
    public static bool TryPick(Camera camera, Vector3d fallback, out Vector3d world)
    {
        world = fallback;
        if (camera == null) return false;

        var ray = ScreenRay(camera, Input.mousePosition);
        var hits = Physics.RaycastAll(ray, 200000f, ~0, QueryTriggerInteraction.Ignore);
        var best = float.MaxValue;
        var found = false;

        foreach (var hit in hits)
        {
            if (hit.collider == null) continue;
            if (hit.collider.GetComponentInParent<Vessel>() != null) continue;
            if (hit.distance >= best) continue;
            best = hit.distance;
            world = RenderToWorld(hit.point);
            found = true;
        }

        if (found) return true;

        var anchor = WorldToRender(fallback);
        var direction = ray.direction;
        var offset = anchor - ray.origin;
        var along = Vector3.Dot(offset, direction);
        if (along < 1f) along = 1f;
        world = RenderToWorld(ray.origin + direction * along);
        return true;
    }

    public static void ToLatLonAlt(CelestialBody body, Vector3d world, out double lat, out double lon, out double alt)
    {
        lat = body.GetLatitude(world);
        lon = RoutePath.NormalizeLon(body.GetLongitude(world));
        alt = body.GetAltitude(world);
    }

    public static Vector3d GroundPoint(CelestialBody body, double lat, double lon)
    {
        var terrain = body.TerrainAltitude(lat, lon, true);
        return body.GetWorldSurfacePosition(lat, lon, terrain);
    }

    /// <summary>Moves a point across the tangent plane through its ground position.</summary>
    public static bool TryGroundMove(CelestialBody body, Camera camera, Vector2 screen, Vector3d groundPoint, out Vector3d world)
    {
        var normal = (groundPoint - body.position).normalized;
        return TryGroundMove(body, camera, screen, groundPoint, normal, out world);
    }

    /// <summary>Same, with the plane fixed by the caller so a drag survives a miss.</summary>
    public static bool TryGroundMove(CelestialBody body, Camera camera, Vector2 screen,
        Vector3d planePoint, Vector3d planeNormal, out Vector3d world)
    {
        world = planePoint;
        if (camera == null) return false;

        var ray = ScreenRay(camera, screen);
        var denominator = Vector3d.Dot(ray.direction, planeNormal);
        if (System.Math.Abs(denominator) < 1e-4) return false;

        var t = Vector3d.Dot(planePoint - (Vector3d)ray.origin, planeNormal) / denominator;
        if (t < 0.0) return false;
        world = (Vector3d)ray.origin + (Vector3d)ray.direction * t;
        return true;
    }

    /// <summary>Moves a point along the local vertical: closest point on the line to the cursor ray.</summary>
    public static bool TryVerticalMove(CelestialBody body, Camera camera, Vector2 screen, Vector3d point, out Vector3d world)
    {
        world = point;
        if (camera == null) return false;

        var up = (point - body.position).normalized;
        var ray = ScreenRay(camera, screen);
        var v = (Vector3d)ray.direction;

        var a = Vector3d.Dot(up, up);
        var b = Vector3d.Dot(up, v);
        var c = Vector3d.Dot(v, v);
        var denominator = a * c - b * b;
        if (System.Math.Abs(denominator) < 1e-6) return false;

        var w0 = point - (Vector3d)ray.origin;
        var d = Vector3d.Dot(up, w0);
        var e = Vector3d.Dot(v, w0);
        var s = (b * e - c * d) / denominator;

        world = point + up * s;
        return true;
    }

    /// <summary>Terrain-projected ground point below an arbitrary world point.</summary>
    public static Vector3d GroundBelow(CelestialBody body, Vector3d point)
    {
        var lat = body.GetLatitude(point);
        var lon = RoutePath.NormalizeLon(body.GetLongitude(point));
        return GroundPoint(body, lat, lon);
    }

    /// <summary>
    /// World to render coordinates: the shared-vessel offset that survives a
    /// floating-origin shift. One copy for gates, marker and picking.
    /// </summary>
    public static Vector3 ToRender(Vector3 vesselRender, Vector3d vesselWorld, Vector3d world)
    {
        return vesselRender + (Vector3)(world - vesselWorld);
    }

    private static Vector3d WorldToRender(Vector3d world)
    {
        var vessel = FlightGlobals.ActiveVessel;
        if (vessel == null) return world;
        return ToRender(vessel.transform.position, vessel.GetWorldPos3D(), world);
    }

    private static Vector3d RenderToWorld(Vector3 render)
    {
        var vessel = FlightGlobals.ActiveVessel;
        if (vessel == null) return render;
        return vessel.GetWorldPos3D() + ((Vector3d)render - (Vector3d)vessel.transform.position);
    }
}
