using System;
using System.Globalization;

namespace Glissade3D;

/// <summary>
/// One approach route: the point the path ends at, the direction of flight and
/// the gate profile. Independent of NavUtilities; runways are imported into it.
/// </summary>
public sealed class Route
{
    public const int FormatVersion = 1;

    public string Id = "";
    public string Name = "";
    public string Body = "";
    public double Lat;
    public double Lon;
    public double AltMsl;
    public double HeadingDeg = 90.0;
    public double AngleDeg = 3.0;
    public float MinDistance = 250f;
    public float MaxDistance = 10000f;
    public float Spacing = 500f;
    public float Width = 40f;
    public float Height = 20f;
    public float EndWidth = 40f;
    public float EndHeight = 20f;
    public float TaperDistance;

    // Runtime flags, never serialised.
    public bool BuiltIn;
    public bool AngleFromDefault;

    public string DisplayName => string.IsNullOrEmpty(Name) ? Id : Name;

    public double EffectiveAngle(double defaultAngle) => AngleFromDefault ? defaultAngle : AngleDeg;

    public Route Clone()
    {
        var copy = new Route();
        copy.CopyFrom(this);
        copy.BuiltIn = BuiltIn;
        copy.AngleFromDefault = AngleFromDefault;
        return copy;
    }

    public void CopyFrom(Route other)
    {
        if (other == null) return;
        Id = other.Id;
        Name = other.Name;
        Body = other.Body;
        Lat = other.Lat;
        Lon = other.Lon;
        AltMsl = other.AltMsl;
        HeadingDeg = other.HeadingDeg;
        AngleDeg = other.AngleDeg;
        MinDistance = other.MinDistance;
        MaxDistance = other.MaxDistance;
        Spacing = other.Spacing;
        Width = other.Width;
        Height = other.Height;
        EndWidth = other.EndWidth;
        EndHeight = other.EndHeight;
        TaperDistance = other.TaperDistance;
    }

    public bool SameData(Route other)
    {
        if (other == null) return false;
        return Id == other.Id
               && Name == other.Name
               && Body == other.Body
               && Nearly(Lat, other.Lat, 1e-9)
               && Nearly(Lon, other.Lon, 1e-9)
               && Nearly(AltMsl, other.AltMsl, 1e-3)
               && Nearly(HeadingDeg, other.HeadingDeg, 1e-6)
               && Nearly(AngleDeg, other.AngleDeg, 1e-6)
               && Nearly(MinDistance, other.MinDistance, 1e-3)
               && Nearly(MaxDistance, other.MaxDistance, 1e-3)
               && Nearly(Spacing, other.Spacing, 1e-3)
               && Nearly(Width, other.Width, 1e-3)
               && Nearly(Height, other.Height, 1e-3)
               && Nearly(EndWidth, other.EndWidth, 1e-3)
               && Nearly(EndHeight, other.EndHeight, 1e-3)
               && Nearly(TaperDistance, other.TaperDistance, 1e-3);
    }

    public void WriteTo(ConfigNode node)
    {
        node.AddValue("id", Id);
        node.AddValue("name", Name);
        node.AddValue("body", Body);
        node.AddValue("lat", Num(Lat, "0.#########"));
        node.AddValue("lon", Num(Lon, "0.#########"));
        node.AddValue("alt", Num(AltMsl, "0.###"));
        node.AddValue("heading", Num(HeadingDeg, "0.######"));
        node.AddValue("angle", Num(AngleDeg, "0.######"));
        node.AddValue("minDistance", Num(MinDistance, "0.###"));
        node.AddValue("maxDistance", Num(MaxDistance, "0.###"));
        node.AddValue("spacing", Num(Spacing, "0.###"));
        node.AddValue("width", Num(Width, "0.###"));
        node.AddValue("height", Num(Height, "0.###"));
        node.AddValue("endWidth", Num(EndWidth, "0.###"));
        node.AddValue("endHeight", Num(EndHeight, "0.###"));
        node.AddValue("taperDistance", Num(TaperDistance, "0.###"));
    }

    public static Route ReadFrom(ConfigNode node)
    {
        if (node == null) return null;

        var route = new Route();
        route.Id = Value(node, "id", "");
        route.Name = Value(node, "name", "");
        route.Body = Value(node, "body", "");
        if (route.Body.Length == 0) return null;

        if (!TryNum(node, "lat", out route.Lat)) return null;
        if (!TryNum(node, "lon", out route.Lon)) return null;
        TryNum(node, "alt", out route.AltMsl);
        if (TryNum(node, "heading", out var heading)) route.HeadingDeg = heading;
        if (TryNum(node, "angle", out var angle)) route.AngleDeg = angle;
        if (TryNum(node, "minDistance", out var min)) route.MinDistance = (float)min;
        if (TryNum(node, "maxDistance", out var max)) route.MaxDistance = (float)max;
        if (TryNum(node, "spacing", out var spacing)) route.Spacing = (float)spacing;
        if (TryNum(node, "width", out var width)) route.Width = (float)width;
        if (TryNum(node, "height", out var height)) route.Height = (float)height;
        if (TryNum(node, "endWidth", out var endWidth)) route.EndWidth = (float)endWidth;
        if (TryNum(node, "endHeight", out var endHeight)) route.EndHeight = (float)endHeight;
        if (TryNum(node, "taperDistance", out var taper)) route.TaperDistance = (float)taper;

        if (string.IsNullOrEmpty(route.Id)) route.Id = NewId();
        if (string.IsNullOrEmpty(route.Name)) route.Name = route.Id;
        return route;
    }

    public static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 12);

    private static string Value(ConfigNode node, string key, string fallback)
    {
        var raw = node.GetValue(key);
        return string.IsNullOrEmpty(raw) ? fallback : raw.Trim();
    }

    private static string Num(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);

    private static bool TryNum(ConfigNode node, string key, out double value)
    {
        value = 0;
        var raw = node.GetValue(key);
        if (string.IsNullOrEmpty(raw)) return false;
        if (!double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return false;
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static bool Nearly(double a, double b, double tolerance) => Math.Abs(a - b) <= tolerance;
}
