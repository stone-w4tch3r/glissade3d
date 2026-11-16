using System;

namespace Glissade3D;

public readonly struct Vec
{
    public readonly double X;
    public readonly double Y;
    public readonly double Z;

    public Vec(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static Vec operator +(Vec a, Vec b) => new Vec(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec operator -(Vec a, Vec b) => new Vec(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec operator -(Vec a) => new Vec(-a.X, -a.Y, -a.Z);
    public static Vec operator *(Vec a, double k) => new Vec(a.X * k, a.Y * k, a.Z * k);

    public static double Dot(Vec a, Vec b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static Vec Cross(Vec a, Vec b) => new Vec(
        a.Y * b.Z - a.Z * b.Y,
        a.Z * b.X - a.X * b.Z,
        a.X * b.Y - a.Y * b.X);

    public double Length => Math.Sqrt(Dot(this, this));

    public Vec Normalized
    {
        get
        {
            var length = Length;
            return length < 1e-12 ? new Vec(0, 0, 0) : new Vec(X / length, Y / length, Z / length);
        }
    }
}

public readonly record struct Deviation(double Along, double Lateral, double Vertical);

public readonly record struct GateFrame(Vec Center, Vec Forward, Vec Up);

public sealed class ApproachPath
{
    private readonly double _rad;

    public ApproachPath(Vec origin, Vec heading, Vec up, double angleDeg)
    {
        Origin = origin;
        Heading = heading.Normalized;
        Up = up.Normalized;
        AngleDeg = angleDeg;
        _rad = angleDeg * Math.PI / 180.0;
    }

    public Vec Origin { get; }
    public Vec Heading { get; }
    public Vec Up { get; }
    public double AngleDeg { get; }

    public double Tan => Math.Tan(_rad);
    public Vec Back => -Heading;
    public Vec FlightDirection => (Heading * Math.Cos(_rad) - Up * Math.Sin(_rad)).Normalized;
    public Vec AlongBack => -FlightDirection;
    public Vec Right => Vec.Cross(Up, Heading).Normalized;
    public Vec UpPerp => Vec.Cross(Right, AlongBack).Normalized;

    public Vec PointAt(double slant) => Origin + AlongBack * slant;

    public GateFrame GateAt(double slant)
    {
        var forward = FlightDirection;
        var perpendicular = Up - forward * Vec.Dot(Up, forward);
        // A path that is vertical has no usable "up" from the local radial, so
        // fall back to the horizontal right vector; otherwise the frame is NaN.
        var up = perpendicular.Length < 1e-4 ? Right : perpendicular.Normalized;
        return new GateFrame(PointAt(slant), forward, up);
    }

    public Deviation Deviate(Vec position)
    {
        var relative = position - Origin;
        var along = Vec.Dot(relative, AlongBack);
        var offset = relative - AlongBack * along;
        return new Deviation(along, Vec.Dot(offset, Right), Vec.Dot(offset, UpPerp));
    }

    public double SlantFrom(double horizontalDistance) => horizontalDistance / Math.Cos(_rad);

    public double HorizontalFrom(double slant) => slant * Math.Cos(_rad);
}
