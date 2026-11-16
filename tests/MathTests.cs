using System;
using Glissade3D;

static class MathTests
{
    private static int _failed;

    public static int Main()
    {
        TestPointAt();
        TestGateOrientation();
        TestDeviationOnPath();
        TestDeviationOffset();
        TestHorizontal();
        TestVerticalGateFrame();
        TestMarkerScale();
        Console.WriteLine(_failed == 0 ? "OK: all math checks passed" : $"FAILED: {_failed}");
        return _failed == 0 ? 0 : 1;
    }

    private static void TestPointAt()
    {
        var path = MakePath(3.0);
        var rad = 3.0 * Math.PI / 180.0;
        var p = path.PointAt(1000);
        ExpectNear("point height", p.Y, 1000 * Math.Sin(rad));
        ExpectNear("point distance", Math.Sqrt(p.X * p.X + p.Z * p.Z), 1000 * Math.Cos(rad));
    }

    private static void TestGateOrientation()
    {
        var frame = MakePath(3.0).GateAt(500);
        ExpectNear("gate up dot forward", Vec.Dot(frame.Up, frame.Forward), 0, 1e-9);
        ExpectTrue("gate up is up", frame.Up.Y > 0.99);
        ExpectTrue("gate forward descends", frame.Forward.Y < 0);
    }

    private static void TestDeviationOnPath()
    {
        var path = MakePath(3.0);
        var deviation = path.Deviate(path.PointAt(2000));
        ExpectNear("along", deviation.Along, 2000, 1e-6);
        ExpectNear("lateral", deviation.Lateral, 0, 1e-6);
        ExpectNear("vertical", deviation.Vertical, 0, 1e-6);
    }

    private static void TestDeviationOffset()
    {
        var path = MakePath(3.0);
        var onPath = path.PointAt(2000);
        var shifted = onPath + path.Right * 5 + path.UpPerp * (-2);
        var deviation = path.Deviate(shifted);
        ExpectNear("offset lateral", deviation.Lateral, 5, 1e-6);
        ExpectNear("offset vertical", deviation.Vertical, -2, 1e-6);
        ExpectNear("offset along", deviation.Along, 2000, 1e-6);
    }

    private static void TestHorizontal()
    {
        var path = MakePath(6.0);
        ExpectNear("slant from horizontal", path.SlantFrom(1000), 1000 / Math.Cos(6.0 * Math.PI / 180.0), 1e-9);
        ExpectNear("horizontal from slant", path.HorizontalFrom(path.SlantFrom(1000)), 1000, 1e-9);
    }

    private static void TestMarkerScale()
    {
        foreach (var distance in new[] { 50.0, 100.0, 300.0, 1000.0, 3000.0, 10000.0, 30000.0 })
        {
            var span = MarkerScale.Span(distance, 60.0, 1080.0);
            ExpectTrue($"span finite {distance}", !double.IsNaN(span) && span > 0);
            ExpectTrue($"span bounded {distance}", span <= MarkerScale.MaxSpan + 1e-9);
            ExpectTrue($"span covers screen {distance}", MarkerScale.ScreenFraction(distance, 60.0) < 0.25);
        }

        ExpectTrue("pixels decrease with distance", MarkerScale.Pixels(1000) > MarkerScale.Pixels(10000));
        ExpectTrue("pixels upper bound", MarkerScale.Pixels(1) <= MarkerScale.NearPixels + 1e-9);
        ExpectTrue("pixels lower bound", MarkerScale.Pixels(1000000) >= MarkerScale.FarPixels - 1e-9);
    }

    private static void TestVerticalGateFrame()
    {
        foreach (var angle in new[] { 90.0, -90.0 })
        {
            var frame = MakePath(angle).GateAt(100);
            var finite = !double.IsNaN(frame.Up.X) && !double.IsNaN(frame.Up.Y) && !double.IsNaN(frame.Up.Z);
            ExpectTrue($"vertical frame finite {angle}", finite);
            ExpectNear($"vertical frame orthogonal {angle}", Vec.Dot(frame.Up, frame.Forward), 0, 1e-9);
            ExpectNear($"vertical frame unit {angle}", frame.Up.Length, 1, 1e-9);
        }
    }

    private static ApproachPath MakePath(double angle)
    {
        return new ApproachPath(new Vec(0, 0, 0), new Vec(0, 0, 1), new Vec(0, 1, 0), angle);
    }

    private static void ExpectNear(string name, double actual, double expected, double tolerance = 1e-6)
    {
        if (Math.Abs(actual - expected) > tolerance)
        {
            _failed++;
            Console.WriteLine($"FAIL {name}: {actual} != {expected} (tol {tolerance})");
        }
    }

    private static void ExpectTrue(string name, bool condition)
    {
        if (condition) return;
        _failed++;
        Console.WriteLine($"FAIL {name}");
    }
}
