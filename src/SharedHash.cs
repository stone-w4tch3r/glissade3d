using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Glissade3D;

/// <summary>
/// SHA-256 helpers. File hashes verify the download; the route hash is a
/// canonical snapshot used to tell "still exactly as installed" from "edited".
/// </summary>
public static class SharedHash
{
    public static string Of(Route route)
    {
        if (route == null) return "";
        var text = string.Join("|", new[]
        {
            route.Id ?? "",
            route.Name ?? "",
            route.Body ?? "",
            Num(route.Lat),
            Num(route.Lon),
            Num(route.AltMsl),
            Num(route.HeadingDeg),
            Num(route.AngleDeg),
            Num(route.MinDistance),
            Num(route.MaxDistance),
            Num(route.Spacing),
            Num(route.Width),
            Num(route.Height),
            Num(route.EndWidth),
            Num(route.EndHeight),
            Num(route.TaperDistance)
        });
        return Sha256(Encoding.UTF8.GetBytes(text));
    }

    public static string Bytes(byte[] bytes) => Sha256(bytes ?? Array.Empty<byte>());

    private static string Num(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static string Num(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string Sha256(byte[] bytes)
    {
        using (var sha = SHA256.Create())
        {
            var hash = sha.ComputeHash(bytes);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }
}
