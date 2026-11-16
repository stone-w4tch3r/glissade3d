using System;
using System.Collections.Generic;

namespace Glissade3D;

public sealed class RouteProblem
{
    public bool IsError;
    public string Key;

    public RouteProblem(bool isError, string key)
    {
        IsError = isError;
        Key = key;
    }
}

/// <summary>
/// Pure route maths: gate layout, taper, two-point construction and validation.
/// No UnityEngine, no CelestialBody — everything here is unit-testable.
/// </summary>
public static class RouteMath
{
    public const double MinAngle = -90.0;
    public const double MaxAngle = 90.0;
    public const float MinSpacing = 1f;
    public const float MaxSpacing = 5000f;
    public const float MinSize = 0.5f;
    public const int MaxGates = 201;
    public const int ComfortableGates = 80;

    /// <summary>Gate 0 always sits at the anchor; the rest run from MinDistance.</summary>
    public static int GateCount(Route route) => Math.Min(RequestedGateCount(route), MaxGates);

    /// <summary>How many gates the profile asks for, before the display cap.</summary>
    public static int RequestedGateCount(Route route)
    {
        if (route == null) return 0;
        if (!IsFinite(route.MinDistance) || !IsFinite(route.MaxDistance)) return 1;
        if (route.MaxDistance <= route.MinDistance) return 1;

        var steps = (double)(route.MaxDistance - route.MinDistance) / SpacingOf(route);
        if (steps >= int.MaxValue - 2.0) return int.MaxValue;
        return 2 + (int)Math.Floor(steps);
    }

    public static float SlantOf(Route route, int index)
    {
        if (index <= 0) return 0f;
        return route.MinDistance + (index - 1) * SpacingOf(route);
    }

    private static float SpacingOf(Route route)
    {
        var spacing = route.Spacing;
        return IsFinite(spacing) && spacing > 0.5f ? spacing : 0.5f;
    }

    public static double NormalizeLon(double lon)
    {
        var value = lon % 360.0;
        return value < 0.0 ? value + 360.0 : value;
    }

    public static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    public static bool IsFiniteRoute(Route route)
    {
        return route != null
               && IsFinite(route.Lat) && IsFinite(route.Lon) && IsFinite(route.AltMsl)
               && IsFinite(route.HeadingDeg) && IsFinite(route.AngleDeg)
               && IsFinite(route.MinDistance) && IsFinite(route.MaxDistance)
               && IsFinite(route.Spacing) && IsFinite(route.Width) && IsFinite(route.Height)
               && IsFinite(route.EndWidth) && IsFinite(route.EndHeight) && IsFinite(route.TaperDistance);
    }

    /// <summary>
    /// 0 = entry size, 1 = final size. Constant before the taper window, then
    /// smoothstep, so the change never starts or stops with a visible kink.
    /// </summary>
    public static float TaperFactor(Route route, float slant)
    {
        if (route == null || route.TaperDistance <= 0.01f) return 0f;

        var u = 1f - Clamp01(slant / route.TaperDistance);
        return u * u * (3f - 2f * u);
    }

    public static void SizeAt(Route route, float slant, out float width, out float height)
    {
        var t = TaperFactor(route, slant);
        width = route.Width + (route.EndWidth - route.Width) * t;
        height = route.Height + (route.EndHeight - route.Height) * t;
    }

    public static double BearingDeg(double lat1, double lon1, double lat2, double lon2)
    {
        var phi1 = lat1 * Math.PI / 180.0;
        var phi2 = lat2 * Math.PI / 180.0;
        var deltaLon = (lon2 - lon1) * Math.PI / 180.0;
        var y = Math.Sin(deltaLon) * Math.Cos(phi2);
        var x = Math.Cos(phi1) * Math.Sin(phi2) - Math.Sin(phi1) * Math.Cos(phi2) * Math.Cos(deltaLon);
        var degrees = Math.Atan2(y, x) * 180.0 / Math.PI;
        return (degrees + 360.0) % 360.0;
    }

    public static double DistanceMeters(double lat1, double lon1, double lat2, double lon2, double radius)
    {
        var phi1 = lat1 * Math.PI / 180.0;
        var phi2 = lat2 * Math.PI / 180.0;
        var dPhi = (lat2 - lat1) * Math.PI / 180.0;
        var dLon = (lon2 - lon1) * Math.PI / 180.0;
        var a = Math.Sin(dPhi * 0.5) * Math.Sin(dPhi * 0.5)
                + Math.Cos(phi1) * Math.Cos(phi2) * Math.Sin(dLon * 0.5) * Math.Sin(dLon * 0.5);
        return 2.0 * radius * Math.Asin(Math.Min(1.0, Math.Sqrt(a)));
    }

    /// <summary>
    /// Two points: A is the far end of the path, B is the anchor it ends at.
    /// Heading, angle and length are written into the same fields the numeric
    /// editor shows, so both input methods share one model.
    /// </summary>
    public static bool FromTwoPoints(Route target, double latA, double lonA, double altA,
        double latB, double lonB, double altB, double bodyRadius, out string error)
    {
        error = "";
        if (target == null)
        {
            error = "no_route";
            return false;
        }

        if (!IsFinite(latA) || !IsFinite(lonA) || !IsFinite(altA)
            || !IsFinite(latB) || !IsFinite(lonB) || !IsFinite(altB)
            || !IsFinite(bodyRadius) || bodyRadius <= 0.0)
        {
            error = "err_bad_point";
            return false;
        }

        var horizontal = DistanceMeters(latA, lonA, latB, lonB, bodyRadius);
        if (horizontal < 5.0)
        {
            error = "points_too_close";
            return false;
        }

        var drop = altA - altB;
        var angle = Math.Atan2(drop, horizontal) * 180.0 / Math.PI;
        angle = Math.Max(MinAngle, Math.Min(MaxAngle, angle));

        var slant = Math.Sqrt(horizontal * horizontal + drop * drop);

        target.Lat = latB;
        target.Lon = NormalizeLon(lonB);
        target.AltMsl = altB;
        target.HeadingDeg = BearingDeg(latA, lonA, latB, lonB);
        target.AngleDeg = angle;
        target.MaxDistance = (float)Math.Max(slant, MinSpacing * 2);
        if (target.MinDistance >= target.MaxDistance) target.MinDistance = target.MaxDistance * 0.1f;
        return true;
    }

    public static List<RouteProblem> Validate(Route route)
    {
        var problems = new List<RouteProblem>();
        Validate(route, problems);
        return problems;
    }

    /// <summary>Same checks, appending into an existing list so IMGUI can cache it.</summary>
    public static void Validate(Route route, List<RouteProblem> problems)
    {
        if (problems == null) return;
        if (route == null)
        {
            problems.Add(new RouteProblem(true, "err_no_route"));
            return;
        }

        if (string.IsNullOrEmpty(route.Body)) problems.Add(new RouteProblem(true, "err_no_body"));
        if (!IsFinite(route.Lat) || !IsFinite(route.Lon) || !IsFinite(route.AltMsl))
            problems.Add(new RouteProblem(true, "err_bad_point"));
        if (!IsFinite(route.HeadingDeg) || route.HeadingDeg < 0.0 || route.HeadingDeg > 360.0)
            problems.Add(new RouteProblem(true, "err_heading"));
        if (!IsFinite(route.AngleDeg) || route.AngleDeg < MinAngle || route.AngleDeg > MaxAngle)
            problems.Add(new RouteProblem(true, "err_angle"));
        if (!IsFinite(route.MinDistance) || !IsFinite(route.MaxDistance) || route.MaxDistance <= route.MinDistance)
            problems.Add(new RouteProblem(true, "err_distance"));
        if (!IsFinite(route.Spacing) || route.Spacing < MinSpacing)
            problems.Add(new RouteProblem(true, "err_spacing"));
        else if (route.Spacing > MaxSpacing)
            problems.Add(new RouteProblem(false, "warn_spacing_large"));
        if (!IsFinite(route.Width) || !IsFinite(route.Height) || route.Width < MinSize || route.Height < MinSize)
            problems.Add(new RouteProblem(true, "err_size"));
        if (!IsFinite(route.EndWidth) || !IsFinite(route.EndHeight) || route.EndWidth < MinSize || route.EndHeight < MinSize)
            problems.Add(new RouteProblem(true, "err_end_size"));
        if (!IsFinite(route.TaperDistance) || route.TaperDistance < 0f || route.TaperDistance > route.MaxDistance)
            problems.Add(new RouteProblem(true, "err_taper"));
        if (route.EndWidth > route.Width || route.EndHeight > route.Height)
            problems.Add(new RouteProblem(false, "warn_taper_grows"));
        if (route.MinDistance < 0f) problems.Add(new RouteProblem(false, "warn_min_distance"));

        var requested = RequestedGateCount(route);
        if (requested > MaxGates) problems.Add(new RouteProblem(true, "err_too_many_gates"));
        else if (requested > ComfortableGates) problems.Add(new RouteProblem(false, "warn_many_gates"));

        if (route.Spacing < route.Width) problems.Add(new RouteProblem(false, "warn_gates_dense"));
    }

    public static bool HasErrors(List<RouteProblem> problems)
    {
        foreach (var problem in problems)
        {
            if (problem.IsError) return true;
        }
        return false;
    }

    private static float Clamp01(float value) => value < 0f ? 0f : (value > 1f ? 1f : value);
}
