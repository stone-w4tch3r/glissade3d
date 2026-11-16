using System;

namespace Glissade3D;

/// <summary>
/// Marker size in screen terms. The marker is sized to a pixel budget, then
/// converted to metres through the camera, so its screen size is bounded by
/// construction and can never fill a quarter of the screen at mid range.
/// </summary>
public static class MarkerScale
{
    public const double NearPixels = 220.0;
    public const double FarPixels = 24.0;
    public const double MinSpan = 0.6;
    public const double MaxSpan = 400.0;

    public static double Pixels(double distance)
    {
        var d = Math.Max(distance, 100.0);
        return Clamp(7000.0 / Math.Sqrt(d), FarPixels, NearPixels);
    }

    public static double WorldPerPixel(double distance, double fovDeg, double screenHeight)
    {
        var fov = fovDeg <= 0.0 ? 60.0 : fovDeg;
        var height = Math.Max(screenHeight, 1.0);
        return 2.0 * Math.Max(distance, 1.0) * Math.Tan(fov * 0.5 * Math.PI / 180.0) / height;
    }

    public static double Span(double distance, double fovDeg, double screenHeight)
    {
        var span = Pixels(distance) * WorldPerPixel(distance, fovDeg, screenHeight);
        return Clamp(span, MinSpan, MaxSpan);
    }

    /// <summary>Fraction of the screen height the marker occupies.</summary>
    public static double ScreenFraction(double distance, double fovDeg)
    {
        var fov = fovDeg <= 0.0 ? 60.0 : fovDeg;
        var visible = 2.0 * Math.Max(distance, 1.0) * Math.Tan(fov * 0.5 * Math.PI / 180.0);
        return Span(distance, fov, 1080.0) / visible;
    }

    private static double Clamp(double value, double min, double max) => value < min ? min : (value > max ? max : value);
}
