using System;
using System.Collections.Generic;
using UnityEngine;

namespace Glissade3D;

/// <summary>
/// The world marker for a route point: an arrow from the sky whose tip touches
/// the sphere, a plumb line down to the ground, and a ring with a crosshair and
/// four outward arrows lying on the terrain.
///
/// Size is a pixel budget converted to metres through the camera, so the marker
/// keeps a predictable screen size: large enough near, bounded at any distance.
/// </summary>
public sealed class MarkerVisual
{
    private sealed class Part
    {
        public GameObject Root;
        public Mesh Mesh;
        public MeshRenderer Renderer;
        public Material Material;
        public float Alpha = 1f;
        public bool Translucent;

        public void SetPose(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            Root.transform.SetPositionAndRotation(position, rotation);
            Root.transform.localScale = scale;
        }

        public void SetActive(bool active)
        {
            if (Root.activeSelf != active) Root.SetActive(active);
        }

        public void Destroy()
        {
            if (Root != null) UnityEngine.Object.Destroy(Root);
            if (Mesh != null) UnityEngine.Object.Destroy(Mesh);
            if (Material != null) UnityEngine.Object.Destroy(Material);
        }
    }

    private readonly Part _cone;
    private readonly Part _stem;
    private readonly Part _plumb;
    private readonly Part _ground;
    private Color _color = new Color(-1f, -1f, -1f, -1f);
    private bool _highlight;
    private bool _visible;
    private readonly string _tag;

    public MarkerVisual(Transform parent, string tag)
    {
        _tag = tag;
        _cone = Create(parent, "GlissadePointArrow", BuildCone(0.5f, 12), false, 1f);
        _stem = Create(parent, "GlissadePointStem", BuildCylinder(0.5f, 8), false, 1f);
        _plumb = Create(parent, "GlissadePlumbLine", BuildCylinder(0.5f, 6), false, 0.8f);
        _ground = Create(parent, "GlissadeGroundMark", BuildGroundMark(), false, 1f);
        SetColor(new Color(1f, 0.85f, 0.15f, 1f));
        SetVisible(false);
    }

    public void SetVisible(bool visible)
    {
        if (_cone.Root == null) return;

        if (_visible != visible)
        {
            _visible = visible;
            Dbg.Line($"marker {_tag} visible={visible}");
        }

        _cone.SetActive(visible);
        _stem.SetActive(visible);
        _plumb.SetActive(visible);
        _ground.SetActive(visible);
    }

    public void SetHighlight(bool highlight)
    {
        if (highlight == _highlight) return;
        _highlight = highlight;
        Apply(_cone);
        Apply(_stem);
        Apply(_plumb);
        Apply(_ground);
    }

    public void SetColor(Color color)
    {
        if (color == _color) return;
        _color = color;
        Apply(_cone);
        Apply(_stem);
        Apply(_plumb);
        Apply(_ground);
    }

    public void Update(Vector3 renderOrigin, Vector3d vesselWorld, Vector3d point, Vector3d up,
        Vector3d groundPoint, double distance, double fovDeg, double screenHeight)
    {
        if (_cone.Root == null) return;

        if (double.IsNaN(distance) || double.IsInfinity(distance)
            || double.IsNaN(point.x) || double.IsNaN(point.y) || double.IsNaN(point.z))
        {
            Dbg.Line($"marker {_tag} skipped: bad geometry distance={distance} point={point}");
            return;
        }

        var span = (float)MarkerScale.Span(distance, fovDeg, screenHeight);
        var headRadius = span * 0.07f;
        var headLength = Mathf.Max(span * 0.16f, 2f);
        var stemRadius = Mathf.Max(span * 0.012f, 0.04f);
        var stemLength = Mathf.Max(span * 0.35f, 6f);
        var plumbRadius = Mathf.Max(span * 0.008f, 0.04f);
        var groundRadius = Mathf.Max(6f, span * 0.45f);

        var upV = ToVector3(up).normalized;
        var rotation = StableRotation(upV);

        var headCenter = point + up * (headLength * 0.5);
        _cone.SetPose(Render(renderOrigin, vesselWorld, headCenter), rotation,
            new Vector3(headRadius * 2f, headLength, headRadius * 2f));

        var stemCenter = point + up * (headLength + stemLength * 0.5);
        _stem.SetPose(Render(renderOrigin, vesselWorld, stemCenter), rotation,
            new Vector3(stemRadius * 2f, stemLength, stemRadius * 2f));

        var line = groundPoint - point;
        var length = Math.Max(line.magnitude, 0.5);
        var direction = line.magnitude < 1e-6 ? -upV : ToVector3(line).normalized;
        var lineRotation = Quaternion.FromToRotation(Vector3.up, direction);
        _plumb.SetPose(Render(renderOrigin, vesselWorld, point + line * 0.5), lineRotation,
            new Vector3(plumbRadius * 2f, (float)length, plumbRadius * 2f));

        _ground.SetPose(Render(renderOrigin, vesselWorld, groundPoint + up * 0.6), rotation,
            Vector3.one * groundRadius);
    }

    public void Destroy()
    {
        _cone.Destroy();
        _stem.Destroy();
        _plumb.Destroy();
        _ground.Destroy();
    }

    private static Vector3 Render(Vector3 renderOrigin, Vector3d vesselWorld, Vector3d world)
    {
        return WorldPicker.ToRender(renderOrigin, vesselWorld, world);
    }

    private static Quaternion StableRotation(Vector3 up)
    {
        var forward = Vector3.ProjectOnPlane(Vector3.forward, up);
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.ProjectOnPlane(Vector3.right, up);
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.up;
        return Quaternion.LookRotation(forward.normalized, up);
    }

    private static Vector3 ToVector3(Vec value) => new Vector3((float)value.X, (float)value.Y, (float)value.Z);

    private static Vector3 ToVector3(Vector3d value) => new Vector3((float)value.x, (float)value.y, (float)value.z);

    private void Apply(Part part)
    {
        if (part.Material == null) return;
        if (!part.Material.HasProperty("_Color")) return;

        var color = _color;
        if (_highlight)
        {
            color = Color.Lerp(color, Color.white, 0.25f);
        }

        var alpha = color.a * part.Alpha;
        if (_highlight && part.Translucent) alpha = Mathf.Max(alpha, 0.85f);
        part.Material.color = new Color(color.r, color.g, color.b, alpha);
    }

    private static Part Create(Transform parent, string name, Mesh mesh, bool translucent, float alpha)
    {
        var part = new Part { Mesh = mesh, Alpha = alpha, Translucent = translucent };
        part.Root = new GameObject(name);
        part.Root.transform.SetParent(parent, false);

        var filter = part.Root.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;

        part.Renderer = part.Root.AddComponent<MeshRenderer>();
        Materials.Configure(part.Renderer);

        part.Material = Materials.CreateInstance(translucent ? Materials.Translucent : Materials.Shared);
        if (part.Material == null) part.Material = Materials.CreateInstance(Materials.Shared);
        if (part.Material != null)
        {
            part.Renderer.sharedMaterial = part.Material;
        }
        else
        {
            part.Root.SetActive(false);
            Dbg.Line($"marker part {name} has no material");
        }

        return part;
    }

    private static Mesh BuildCone(float radius, int segments)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        var tip = new Vector3(0f, -0.5f, 0f);
        var baseY = 0.5f;

        for (var i = 0; i < segments; i++)
        {
            var a0 = i * Mathf.PI * 2f / segments;
            var a1 = (i + 1) * Mathf.PI * 2f / segments;
            var index = vertices.Count;
            vertices.Add(tip);
            vertices.Add(new Vector3(Mathf.Cos(a0) * radius, baseY, Mathf.Sin(a0) * radius));
            vertices.Add(new Vector3(Mathf.Cos(a1) * radius, baseY, Mathf.Sin(a1) * radius));
            AddTriangle(triangles, index, index + 1, index + 2);
        }

        return Finalize("GlissadePointConeMesh", vertices, triangles);
    }

    private static Mesh BuildCylinder(float radius, int segments)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();

        for (var i = 0; i < segments; i++)
        {
            var a0 = i * Mathf.PI * 2f / segments;
            var a1 = (i + 1) * Mathf.PI * 2f / segments;
            var x0 = Mathf.Cos(a0) * radius;
            var z0 = Mathf.Sin(a0) * radius;
            var x1 = Mathf.Cos(a1) * radius;
            var z1 = Mathf.Sin(a1) * radius;

            var index = vertices.Count;
            vertices.Add(new Vector3(x0, -0.5f, z0));
            vertices.Add(new Vector3(x1, -0.5f, z1));
            vertices.Add(new Vector3(x1, 0.5f, z1));
            vertices.Add(new Vector3(x0, 0.5f, z0));
            AddQuad(triangles, index);
        }

        return Finalize("GlissadePointCylinderMesh", vertices, triangles);
    }

    /// <summary>
    /// Cross with arrowheads at the four ends and a circle inside it: the shape
    /// reads as "grab and move" without any extra text.
    /// </summary>
    private static Mesh BuildGroundMark()
    {
        const float arm = 1.0f;
        const float barHalf = 0.03f;
        const float ringOuter = 0.55f;
        const float ringInner = 0.49f;
        const float headStart = 0.74f;
        const float headHalf = 0.15f;

        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        const int segments = 36;

        for (var i = 0; i < segments; i++)
        {
            var a0 = i * Mathf.PI * 2f / segments;
            var a1 = (i + 1) * Mathf.PI * 2f / segments;

            var index = vertices.Count;
            vertices.Add(new Vector3(Mathf.Cos(a0) * ringInner, 0f, Mathf.Sin(a0) * ringInner));
            vertices.Add(new Vector3(Mathf.Cos(a0) * ringOuter, 0f, Mathf.Sin(a0) * ringOuter));
            vertices.Add(new Vector3(Mathf.Cos(a1) * ringOuter, 0f, Mathf.Sin(a1) * ringOuter));
            vertices.Add(new Vector3(Mathf.Cos(a1) * ringInner, 0f, Mathf.Sin(a1) * ringInner));
            AddQuad(triangles, index);
        }

        AddBar(vertices, triangles, -arm, -barHalf, arm, barHalf);
        AddBar(vertices, triangles, -barHalf, -arm, barHalf, arm);

        AddHead(vertices, triangles, new Vector3(1f, 0f, 0f), headStart, headHalf, arm);
        AddHead(vertices, triangles, new Vector3(-1f, 0f, 0f), headStart, headHalf, arm);
        AddHead(vertices, triangles, new Vector3(0f, 0f, 1f), headStart, headHalf, arm);
        AddHead(vertices, triangles, new Vector3(0f, 0f, -1f), headStart, headHalf, arm);

        return Finalize("GlissadeGroundMarkMesh", vertices, triangles);
    }

    private static void AddHead(List<Vector3> vertices, List<int> triangles, Vector3 direction,
        float headStart, float headHalf, float tip)
    {
        var side = new Vector3(-direction.z, 0f, direction.x);
        var index = vertices.Count;
        vertices.Add(direction * headStart + side * headHalf);
        vertices.Add(direction * headStart - side * headHalf);
        vertices.Add(direction * tip);
        AddTriangle(triangles, index, index + 1, index + 2);
    }

    private static void AddBar(List<Vector3> vertices, List<int> triangles, float x0, float z0, float x1, float z1)
    {
        var index = vertices.Count;
        vertices.Add(new Vector3(x0, 0f, z0));
        vertices.Add(new Vector3(x1, 0f, z0));
        vertices.Add(new Vector3(x1, 0f, z1));
        vertices.Add(new Vector3(x0, 0f, z1));
        AddQuad(triangles, index);
    }

    private static void AddTriangle(List<int> triangles, int a, int b, int c)
    {
        triangles.Add(a);
        triangles.Add(b);
        triangles.Add(c);
        triangles.Add(c);
        triangles.Add(b);
        triangles.Add(a);
    }

    private static void AddQuad(List<int> triangles, int index)
    {
        AddTriangle(triangles, index, index + 1, index + 2);
        AddTriangle(triangles, index, index + 2, index + 3);
    }

    private static Mesh Finalize(string name, List<Vector3> vertices, List<int> triangles)
    {
        var mesh = new Mesh { name = name };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }
}
