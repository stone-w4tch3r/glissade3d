using System.Globalization;

namespace Glissade3D;

public sealed class RunwayInfo
{
    public string Ident = "";
    public string ShortId = "";
    public string Body = "";
    public string NodeName = "";
    public double HeadingDeg;
    public double AltMsl;
    public double GsLat;
    public double GsLon;
    public double LocLat;
    public double LocLon;
    public double OuterMarker = 10000;
    public double MiddleMarker = 2000;
    public double InnerMarker = 200;
    public bool Custom;

    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrEmpty(ShortId)) return ShortId;
            if (!string.IsNullOrEmpty(Ident) && !Ident.StartsWith("#")) return Ident;
            return "RWY";
        }
    }

    public string ListLabel => $"{DisplayName}  {HeadingDeg:0.#}°  {AltMsl:0} m";

    public static bool TryParse(ConfigNode node, out RunwayInfo info)
    {
        info = new RunwayInfo();
        if (node == null) return false;

        info.NodeName = node.name;
        info.Ident = node.GetValue("ident") ?? "";
        info.ShortId = node.GetValue("shortID") ?? "";
        info.Body = node.GetValue("body") ?? "";

        if (info.Body.Length == 0) return false;
        if (!TryNum(node, "hdg", out info.HeadingDeg)) return false;
        if (!TryNum(node, "gsLatitude", out info.GsLat)) return false;
        if (!TryNum(node, "gsLongitude", out info.GsLon)) return false;

        TryNum(node, "altMSL", out info.AltMsl);
        TryNum(node, "locLatitude", out info.LocLat);
        TryNum(node, "locLongitude", out info.LocLon);
        if (TryNum(node, "outerMarkerDist", out var om)) info.OuterMarker = om;
        if (TryNum(node, "middleMarkerDist", out var mm)) info.MiddleMarker = mm;
        if (TryNum(node, "innerMarkerDist", out var im)) info.InnerMarker = im;

        var custom = node.GetValue("custom") ?? "";
        info.Custom = custom.Equals("true", System.StringComparison.OrdinalIgnoreCase) || custom == "1";

        return true;
    }

    private static bool TryNum(ConfigNode node, string key, out double value)
    {
        value = 0;
        var raw = node.GetValue(key);
        if (string.IsNullOrEmpty(raw)) return false;
        return double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
