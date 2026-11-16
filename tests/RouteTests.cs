using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Glissade3D;

static class RouteTests
{
    private static int _failed;
    private static readonly string Managed = ResolveManaged();

    public static int Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;

        TestFormatRoundTrip();
        TestFormatTolerance();
        TestMerge();
        TestDuplicate();
        TestFromRunway();
        TestTaper();
        TestGateLayout();
        TestTwoPoints();
        TestClimbingTwoPoints();
        TestTwoPointsBadInput();
        TestNormalizeLon();
        TestGateCap();
        TestTooManyGates();
        TestDraft();
        TestValidation();

        Console.WriteLine(_failed == 0 ? "OK: all route checks passed" : $"FAILED: {_failed}");
        return _failed == 0 ? 0 : 1;
    }

    private static void TestFormatRoundTrip()
    {
        var route = Sample();
        var node = new ConfigNode("Route");
        route.WriteTo(node);

        var back = Route.ReadFrom(node);
        ExpectTrue("round-trip parsed", back != null);
        if (back == null) return;
        ExpectTrue("round-trip equal", back.SameData(route));
        ExpectNear("round-trip angle", back.AngleDeg, route.AngleDeg);
        ExpectNear("round-trip taper", back.TaperDistance, route.TaperDistance, 1e-4);
    }

    private static void TestFormatTolerance()
    {
        var node = new ConfigNode("Route");
        node.AddValue("body", "Kerbin");
        node.AddValue("lat", "1.5");
        node.AddValue("lon", "200.25");
        node.AddValue("unknownField", "ignore me");

        var route = Route.ReadFrom(node);
        ExpectTrue("tolerant parse", route != null);
        if (route == null) return;
        ExpectTrue("generated id", !string.IsNullOrEmpty(route.Id));
        ExpectNear("default angle", route.AngleDeg, 3.0);
        ExpectNear("default max", route.MaxDistance, 10000.0);

        var broken = new ConfigNode("Route");
        broken.AddValue("body", "Kerbin");
        broken.AddValue("lat", "not a number");
        broken.AddValue("lon", "1");
        ExpectTrue("bad number rejected", Route.ReadFrom(broken) == null);

        var noBody = new ConfigNode("Route");
        noBody.AddValue("lat", "1");
        noBody.AddValue("lon", "1");
        ExpectTrue("missing body rejected", Route.ReadFrom(noBody) == null);
    }

    private static void TestMerge()
    {
        var builtin = Sample();
        builtin.Id = "builtin";
        builtin.BuiltIn = true;

        var overrideRoute = Sample();
        overrideRoute.Id = "builtin";
        overrideRoute.Name = "overridden";

        var extra = Sample();
        extra.Id = "user-2";

        var merged = RouteStore.Merge(new[] { builtin }, new[] { overrideRoute, extra });
        ExpectTrue("merge count", merged.Count == 2);
        ExpectTrue("user overrides builtin", merged[0].Name == "overridden");
        ExpectTrue("override is not builtin", !merged[0].BuiltIn);
        ExpectTrue("extra appended", merged[1].Id == "user-2");
    }

    private static void TestDuplicate()
    {
        var source = Sample();
        source.Id = "src";
        source.Name = "Bridge";
        source.BuiltIn = true;
        source.AngleFromDefault = true;

        var existing = new List<Route> { source, Sample() };
        existing[1].Name = "Bridge copy";

        var copy = RouteStore.Duplicate(source, existing);
        ExpectTrue("copy exists", copy != null);
        if (copy == null) return;
        ExpectTrue("copy has new id", copy.Id != source.Id);
        ExpectTrue("copy name unique", copy.Name == "Bridge copy (2)");
        ExpectTrue("copy is editable", !copy.BuiltIn && !copy.AngleFromDefault);
        ExpectNear("copy keeps angle", copy.AngleDeg, source.AngleDeg);
    }

    private static void TestFromRunway()
    {
        var node = new ConfigNode("Runway");
        node.AddValue("ident", "KSC_09");
        node.AddValue("shortID", "SC09");
        node.AddValue("hdg", "90.4");
        node.AddValue("body", "Kerbin");
        node.AddValue("altMSL", "70");
        node.AddValue("gsLatitude", "-0.04877658");
        node.AddValue("gsLongitude", "285.29973537");

        ExpectTrue("runway parsed", RunwayInfo.TryParse(node, out var runway));
        var route = RouteStore.FromRunway(runway, 4.5);
        ExpectTrue("route from runway", route != null);
        if (route == null) return;
        ExpectTrue("builtin flag", route.BuiltIn);
        ExpectTrue("angle follows default", route.AngleFromDefault);
        ExpectNear("converted angle", route.EffectiveAngle(4.5), 4.5);
        ExpectNear("converted heading", route.HeadingDeg, 90.4);
        ExpectTrue("stable id", route.Id == RouteStore.BuiltInId(runway));
    }

    private static void TestTaper()
    {
        var route = Sample();
        route.Width = 40f;
        route.Height = 20f;
        route.EndWidth = 10f;
        route.EndHeight = 6f;
        route.TaperDistance = 300f;

        RouteMath.SizeAt(route, 0f, out var w0, out var h0);
        RouteMath.SizeAt(route, 300f, out var w1, out var h1);
        RouteMath.SizeAt(route, 5000f, out var w2, out _);

        ExpectNear("pinch width", w0, 10.0, 1e-4);
        ExpectNear("pinch height", h0, 6.0, 1e-4);
        ExpectNear("entry width", w1, 40.0, 1e-4);
        ExpectNear("far width", w2, 40.0, 1e-4);

        var previous = -1.0;
        var monotone = true;
        for (var slant = 0f; slant <= 300f; slant += 10f)
        {
            RouteMath.SizeAt(route, slant, out var w, out _);
            if (w < previous - 1e-4) monotone = false;
            previous = w;
        }
        ExpectTrue("taper is monotone", monotone);

        ExpectTrue("taper starts smoothly", RouteMath.TaperFactor(route, 299f) < 0.001f);
        ExpectTrue("taper ends smoothly", RouteMath.TaperFactor(route, 1f) > 0.999f);

        var flat = Sample();
        flat.TaperDistance = 0f;
        RouteMath.SizeAt(flat, 0f, out var flatWidth, out _);
        ExpectNear("no taper keeps entry size", flatWidth, flat.Width, 1e-4);
    }

    private static void TestGateLayout()
    {
        var route = Sample();
        route.MinDistance = 250f;
        route.MaxDistance = 1000f;
        route.Spacing = 250f;

        ExpectTrue("gate count", RouteMath.GateCount(route) == 5);
        ExpectNear("anchor gate at zero", RouteMath.SlantOf(route, 0), 0.0);
        ExpectNear("first gate at min", RouteMath.SlantOf(route, 1), 250.0);
        ExpectNear("last gate at max", RouteMath.SlantOf(route, 4), 1000.0);

        route.MaxDistance = 20f;
        ExpectTrue("degenerate route has anchor gate", RouteMath.GateCount(route) == 1);
    }

    private static void TestTwoPoints()
    {
        var route = Sample();
        var radius = 600000.0;

        var latA = 0.0;
        var lonA = 0.0;
        var latB = 0.0;
        var lonB = 0.1;

        ExpectTrue("two points accepted", RouteMath.FromTwoPoints(route, latA, lonA, 1000.0, latB, lonB, 500.0, radius, out var error));
        ExpectTrue("two points error empty", error.Length == 0);

        var horizontal = RouteMath.DistanceMeters(latA, lonA, latB, lonB, radius);
        var expectedAngle = Math.Atan2(500.0, horizontal) * 180.0 / Math.PI;

        ExpectNear("two points heading", route.HeadingDeg, 90.0, 0.2);
        ExpectNear("two points angle", route.AngleDeg, expectedAngle, 0.2);
        ExpectNear("two points length", route.MaxDistance, Math.Sqrt(horizontal * horizontal + 250000.0), 1.0);
        ExpectNear("anchor is B", route.Lat, latB);
        ExpectNear("anchor lon is B", route.Lon, lonB, 1e-9);
        ExpectNear("anchor altitude is B", route.AltMsl, 500.0, 1e-6);

        ExpectTrue("coincident points rejected", !RouteMath.FromTwoPoints(route, 0, 0, 100, 0, 0, 100, radius, out _));
    }

    private static void TestClimbingTwoPoints()
    {
        var route = Sample();
        var radius = 600000.0;

        ExpectTrue("climbing two points accepted",
            RouteMath.FromTwoPoints(route, 0.0, 0.0, 500.0, 0.0, 0.1, 1000.0, radius, out _));

        var horizontal = RouteMath.DistanceMeters(0.0, 0.0, 0.0, 0.1, radius);
        var expected = -Math.Atan2(500.0, horizontal) * 180.0 / Math.PI;

        ExpectNear("climb angle is negative", route.AngleDeg, expected, 0.2);
        ExpectTrue("climb angle below zero", route.AngleDeg < 0);
        ExpectTrue("climb route is valid", !RouteMath.HasErrors(RouteMath.Validate(route)));
    }

    private static void TestTwoPointsBadInput()
    {
        var route = Sample();
        ExpectTrue("NaN point rejected",
            !RouteMath.FromTwoPoints(route, double.NaN, 0, 100, 0, 0.1, 50, 600000, out var nanError));
        ExpectTrue("NaN point error text", nanError.Length > 0);
        ExpectTrue("zero radius rejected",
            !RouteMath.FromTwoPoints(route, 0, 0, 100, 0, 0.1, 50, 0, out _));
        ExpectTrue("negative radius rejected",
            !RouteMath.FromTwoPoints(route, 0, 0, 100, 0, 0.1, 50, -1, out _));

        ExpectTrue("two points accepted with normalisable lon",
            RouteMath.FromTwoPoints(route, 0, -0.1, 100, 0, 0.1, 50, 600000, out _));
        ExpectTrue("anchor lon is normalised", route.Lon >= 0 && route.Lon < 360);
    }

    private static void TestNormalizeLon()
    {
        ExpectNear("lon -0.1 wraps", RouteMath.NormalizeLon(-0.1), 359.9, 1e-9);
        ExpectNear("lon 360 wraps", RouteMath.NormalizeLon(360.0), 0.0, 1e-9);
        ExpectNear("lon -370 wraps", RouteMath.NormalizeLon(-370.0), 350.0, 1e-9);
    }

    private static void TestGateCap()
    {
        var route = Sample();
        route.MinDistance = 0f;
        route.MaxDistance = 100000f;
        route.Spacing = 1f;

        ExpectTrue("requested count exceeds cap", RouteMath.RequestedGateCount(route) > RouteMath.MaxGates);
        ExpectTrue("gate count is capped", RouteMath.GateCount(route) == RouteMath.MaxGates);

        route.Spacing = 0f;
        ExpectTrue("zero spacing does not overflow", RouteMath.RequestedGateCount(route) > 0);
    }

    private static void TestTooManyGates()
    {
        var route = Sample();
        route.MinDistance = 0f;
        route.MaxDistance = 100000f;
        route.Spacing = 1f;
        ExpectTrue("too many gates error", HasProblem(route, "err_too_many_gates", true));

        route = Sample();
        route.MaxDistance = 100000f;
        route.Spacing = 1000f;
        ExpectTrue("many gates warning", HasProblem(route, "warn_many_gates", false));
        ExpectTrue("many gates is not an error", !RouteMath.HasErrors(RouteMath.Validate(route)));
    }

    private static void TestDraft()
    {
        var source = Sample();
        source.Id = "draft-source";
        source.Name = "source";

        var draft = new RouteDraft(source, false);
        ExpectTrue("fresh draft is clean", !draft.IsDirty);
        ExpectTrue("no undo on fresh draft", !draft.CanUndo);

        draft.BeginChange("angle");
        draft.Draft.AngleDeg = 9.0;
        ExpectTrue("draft is dirty", draft.IsDirty);
        ExpectTrue("undo available", draft.CanUndo);
        ExpectTrue("undo label", draft.LastLabel == "angle");

        draft.Undo();
        ExpectNear("undo restores angle", draft.Draft.AngleDeg, source.AngleDeg);
        ExpectTrue("undo returns to clean", !draft.IsDirty);

        draft.BeginChange("a");
        draft.Draft.AngleDeg = 7.0;
        draft.BeginChange("b");
        draft.Draft.MaxDistance = 5000f;
        draft.Undo();
        ExpectNear("second undo keeps first change", draft.Draft.AngleDeg, 7.0);
        ExpectNear("second undo reverts second change", draft.Draft.MaxDistance, source.MaxDistance);

        draft.Cancel();
        ExpectTrue("cancel clears dirt", !draft.IsDirty);
        ExpectTrue("cancel clears undo", !draft.CanUndo);

        var fresh = new RouteDraft(null, true);
        ExpectTrue("new draft is dirty", fresh.IsDirty);
        ExpectTrue("new draft has id", !string.IsNullOrEmpty(fresh.Draft.Id));
    }

    private static void TestValidation()
    {
        var route = Sample();
        ExpectTrue("sample is valid", !RouteMath.HasErrors(RouteMath.Validate(route)));

        route.AngleDeg = 91.0;
        ExpectTrue("angle error", HasProblem(route, "err_angle", true));

        route = Sample();
        route.AngleDeg = -90.0;
        ExpectTrue("vertical climb accepted", !RouteMath.HasErrors(RouteMath.Validate(route)));

        route = Sample();
        route.AngleDeg = 1.0;
        ExpectTrue("shallow angle accepted", !RouteMath.HasErrors(RouteMath.Validate(route)));

        route = Sample();
        route.Spacing = 0.5f;
        ExpectTrue("spacing error", HasProblem(route, "err_spacing", true));

        route = Sample();
        route.MinDistance = 0f;
        route.MaxDistance = 1000f;
        route.Spacing = 10f;
        ExpectTrue("narrow spacing accepted", !RouteMath.HasErrors(RouteMath.Validate(route)));

        route = Sample();
        route.MaxDistance = 100f;
        route.MinDistance = 500f;
        ExpectTrue("distance error", HasProblem(route, "err_distance", true));

        route = Sample();
        route.EndWidth = 80f;
        ExpectTrue("taper growth warning", HasProblem(route, "warn_taper_grows", false));

        route = Sample();
        route.TaperDistance = route.MaxDistance + 100f;
        ExpectTrue("taper error", HasProblem(route, "err_taper", true));
    }

    private static bool HasProblem(Route route, string key, bool isError)
    {
        foreach (var problem in RouteMath.Validate(route))
        {
            if (problem.Key == key && problem.IsError == isError) return true;
        }
        return false;
    }

    private static Route Sample()
    {
        return new Route
        {
            Id = "sample",
            Name = "sample",
            Body = "Kerbin",
            Lat = -0.0487,
            Lon = 285.3,
            AltMsl = 70,
            HeadingDeg = 90.4,
            AngleDeg = 3.0,
            MinDistance = 250f,
            MaxDistance = 10000f,
            Spacing = 500f,
            Width = 40f,
            Height = 20f,
            EndWidth = 40f,
            EndHeight = 20f,
            TaperDistance = 0f
        };
    }

    private static void ExpectNear(string name, double actual, double expected, double tolerance = 1e-6)
    {
        if (Math.Abs(actual - expected) <= tolerance) return;
        _failed++;
        Console.WriteLine($"FAIL {name}: {actual} != {expected} (tol {tolerance})");
    }

    private static void ExpectTrue(string name, bool condition)
    {
        if (condition) return;
        _failed++;
        Console.WriteLine($"FAIL {name}");
    }

    private static string ResolveManaged()
    {
        var ksp = Environment.GetEnvironmentVariable("KSP_DIR");
        if (string.IsNullOrEmpty(ksp)) throw new InvalidOperationException("Set KSP_DIR to the game folder");
        return Path.Combine(ksp, "KSP_x64_Data", "Managed");
    }

    private static Assembly Resolve(object sender, ResolveEventArgs e)
    {
        var name = new AssemblyName(e.Name).Name + ".dll";
        var candidate = Path.Combine(Managed, name);
        return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
    }
}
