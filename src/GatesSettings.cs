using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Glissade3D;

/// <summary>
/// Global preferences only: the default angle used by read-only routes, the
/// defaults a new route starts from, and window state. Route geometry lives in
/// the route itself.
/// </summary>
public sealed class GatesSettings
{
    public string ActiveRouteId = "";
    public bool AutoRoute = true;

    public double DefaultAngle = 3.0;
    public float DefaultSpacing = 500f;
    public float DefaultWidth = 40f;
    public float DefaultHeight = 20f;
    public float DefaultMinDistance = 250f;
    public float DefaultMaxDistance = 10000f;
    public float DefaultEndWidth = 40f;
    public float DefaultEndHeight = 20f;
    public float DefaultTaperDistance = 0f;

    public bool ShowGates = true;
    public bool HideWhenLanded = true;
    public bool Advanced;
    public bool DebugLog;
    public KeyCode ToggleKey = KeyCode.Y;
    public string SharedBaseUrl = "";

    public bool ShowWindow;
    public float WindowX = 260f;
    public float WindowY = 120f;
    public float WindowW = 540f;
    public float WindowH = 560f;
    public float EditorX = 700f;
    public float EditorY = 120f;
    public float EditorW = 420f;
    public float EditorH = 620f;

    private static string Folder => Path.Combine(KSPUtil.ApplicationRootPath, "PluginData", "Glissade3D");
    private static string FilePath => Path.Combine(Folder, "settings.cfg");

    public static GatesSettings Load()
    {
        var settings = new GatesSettings();
        try
        {
            if (!File.Exists(FilePath)) return settings;
            var file = ConfigIo.Load(FilePath);
            if (file == null) return settings;
            var node = file.HasNode("Glissade3D") ? file.GetNode("Glissade3D") : file;
            settings.ApplyNode(node);

            Debug.Log($"[Glissade3D] settings loaded: angle={settings.DefaultAngle} auto={settings.AutoRoute} active={settings.ActiveRouteId} debug={settings.DebugLog}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[Glissade3D] settings load failed: {e.Message}");
        }
        return settings;
    }

    public void ApplyNode(ConfigNode node)
    {
        ActiveRouteId = Text(node, "activeRouteId", ActiveRouteId);
        AutoRoute = ReadBool(node, "autoRoute", AutoRoute);
        DefaultAngle = ReadDouble(node, "defaultAngle", ReadDouble(node, "angleDeg", DefaultAngle));
        DefaultAngle = Math.Max(RouteMath.MinAngle, Math.Min(RouteMath.MaxAngle, DefaultAngle));
        DefaultSpacing = (float)ReadDouble(node, "defaultSpacing", ReadDouble(node, "spacing", DefaultSpacing));
        DefaultWidth = (float)ReadDouble(node, "defaultWidth", ReadDouble(node, "width", DefaultWidth));
        DefaultHeight = (float)ReadDouble(node, "defaultHeight", ReadDouble(node, "height", DefaultHeight));
        DefaultMinDistance = (float)ReadDouble(node, "defaultMinDistance", ReadDouble(node, "minDistance", DefaultMinDistance));
        DefaultMaxDistance = (float)ReadDouble(node, "defaultMaxDistance", ReadDouble(node, "maxDistance", DefaultMaxDistance));
        DefaultTaperDistance = (float)ReadDouble(node, "defaultTaperDistance", DefaultTaperDistance);
        DefaultEndWidth = (float)ReadDouble(node, "defaultEndWidth", DefaultWidth);
        DefaultEndHeight = (float)ReadDouble(node, "defaultEndHeight", DefaultHeight);

        ShowGates = ReadBool(node, "showGates", ShowGates);
        HideWhenLanded = ReadBool(node, "hideWhenLanded", HideWhenLanded);
        Advanced = ReadBool(node, "advanced", Advanced);
        DebugLog = ReadBool(node, "debugLog", DebugLog);
        ToggleKey = ReadKey(node, "toggleKey", ToggleKey);
        SharedBaseUrl = SharedInstaller.DecodeBaseUrl(Text(node, "sharedBaseUrl", ""));

        WindowX = (float)ReadDouble(node, "windowX", WindowX);
        WindowY = (float)ReadDouble(node, "windowY", WindowY);
        EditorX = (float)ReadDouble(node, "editorX", EditorX);
        EditorY = (float)ReadDouble(node, "editorY", EditorY);
    }

    public ConfigNode ToNode()
    {
        var node = new ConfigNode("Glissade3D");
        node.AddValue("activeRouteId", ActiveRouteId ?? "");
        node.AddValue("autoRoute", AutoRoute.ToString());
        node.AddValue("defaultAngle", Num(DefaultAngle));
        node.AddValue("defaultSpacing", Num(DefaultSpacing));
        node.AddValue("defaultWidth", Num(DefaultWidth));
        node.AddValue("defaultHeight", Num(DefaultHeight));
        node.AddValue("defaultMinDistance", Num(DefaultMinDistance));
        node.AddValue("defaultMaxDistance", Num(DefaultMaxDistance));
        node.AddValue("defaultEndWidth", Num(DefaultEndWidth));
        node.AddValue("defaultEndHeight", Num(DefaultEndHeight));
        node.AddValue("defaultTaperDistance", Num(DefaultTaperDistance));
        node.AddValue("showGates", ShowGates.ToString());
        node.AddValue("hideWhenLanded", HideWhenLanded.ToString());
        node.AddValue("advanced", Advanced.ToString());
        node.AddValue("debugLog", DebugLog.ToString());
        node.AddValue("toggleKey", ToggleKey.ToString());
        node.AddValue("sharedBaseUrl", SharedInstaller.EncodeBaseUrl(SharedBaseUrl));
        node.AddValue("windowX", Num(WindowX));
        node.AddValue("windowY", Num(WindowY));
        node.AddValue("editorX", Num(EditorX));
        node.AddValue("editorY", Num(EditorY));
        return node;
    }

    public void Save()
    {
        var node = ToNode();

        if (ConfigIo.Save(node, FilePath, out _))
        {
            Debug.Log($"[Glissade3D] settings saved: angle={DefaultAngle} auto={AutoRoute} active={ActiveRouteId}");
        }
    }

    public Route NewRouteTemplate(string body, double lat, double lon, double alt, double heading, string name, string id)
    {
        return new Route
        {
            Id = id,
            Name = name,
            Body = body,
            Lat = lat,
            Lon = lon,
            AltMsl = alt,
            HeadingDeg = heading,
            AngleDeg = DefaultAngle,
            MinDistance = DefaultMinDistance,
            MaxDistance = DefaultMaxDistance,
            Spacing = DefaultSpacing,
            Width = DefaultWidth,
            Height = DefaultHeight,
            EndWidth = DefaultEndWidth,
            EndHeight = DefaultEndHeight,
            TaperDistance = DefaultTaperDistance
        };
    }

    public void ResetToDefaults()
    {
        var defaults = new GatesSettings();
        DefaultAngle = defaults.DefaultAngle;
        DefaultSpacing = defaults.DefaultSpacing;
        DefaultWidth = defaults.DefaultWidth;
        DefaultHeight = defaults.DefaultHeight;
        DefaultMinDistance = defaults.DefaultMinDistance;
        DefaultMaxDistance = defaults.DefaultMaxDistance;
        DefaultEndWidth = defaults.DefaultEndWidth;
        DefaultEndHeight = defaults.DefaultEndHeight;
        DefaultTaperDistance = defaults.DefaultTaperDistance;
        ShowGates = defaults.ShowGates;
        HideWhenLanded = defaults.HideWhenLanded;
        Advanced = defaults.Advanced;
        DebugLog = defaults.DebugLog;
        ToggleKey = defaults.ToggleKey;
        WindowX = defaults.WindowX;
        WindowY = defaults.WindowY;
        EditorX = defaults.EditorX;
        EditorY = defaults.EditorY;
    }

    private static string Num(double value) => double.IsNaN(value) || double.IsInfinity(value)
        ? "0"
        : value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Text(ConfigNode node, string key, string fallback)
    {
        var raw = node.GetValue(key);
        return raw == null ? fallback : raw.Trim();
    }

    private static double ReadDouble(ConfigNode node, string key, double fallback)
    {
        var raw = node.GetValue(key);
        if (raw == null) return fallback;
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return fallback;
        return double.IsNaN(value) || double.IsInfinity(value) ? fallback : value;
    }

    private static bool ReadBool(ConfigNode node, string key, bool fallback)
    {
        var raw = node.GetValue(key);
        return bool.TryParse(raw, out var value) ? value : fallback;
    }

    private static KeyCode ReadKey(ConfigNode node, string key, KeyCode fallback)
    {
        var raw = node.GetValue(key);
        return !string.IsNullOrEmpty(raw) && Enum.TryParse(raw, true, out KeyCode value) ? value : fallback;
    }
}
