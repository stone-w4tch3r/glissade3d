using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Glissade3D;

public sealed class GateVisual
{
    private static Material _material;
    private static bool _resolved;
    private static bool _hasColor;

    private readonly GameObject _gameObject;
    private readonly MeshRenderer _renderer;
    private readonly Mesh _mesh;
    private readonly MaterialPropertyBlock _block;
    private Material _instanceMaterial;
    private Color _lastColor = new Color(-1f, -1f, -1f, -1f);
    private float _lastWidth = float.NaN;
    private float _lastHeight = float.NaN;

    public GateVisual(Transform parent)
    {
        EnsureAssets();

        _gameObject = new GameObject("Glissade3DGate");
        _gameObject.transform.SetParent(parent, false);

        _mesh = BuildFrameMesh(1f, 1f, 0.06f);
        var filter = _gameObject.AddComponent<MeshFilter>();
        filter.sharedMesh = _mesh;

        _renderer = _gameObject.AddComponent<MeshRenderer>();
        _renderer.shadowCastingMode = ShadowCastingMode.Off;
        _renderer.receiveShadows = false;
        _renderer.lightProbeUsage = LightProbeUsage.Off;
        _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

        if (_material == null)
        {
            _gameObject.SetActive(false);
            return;
        }

        if (_hasColor)
        {
            _block = new MaterialPropertyBlock();
            _renderer.sharedMaterial = _material;
        }
        else
        {
            _instanceMaterial = new Material(_material);
            _renderer.sharedMaterial = _instanceMaterial;
        }
    }

    /// <summary>
    /// The mesh is a unit frame; width, height and thickness come from the
    /// transform. That keeps profile edits instant and identical to point edits.
    /// </summary>
    public void SetSize(float width, float height)
    {
        if (Mathf.Approximately(width, _lastWidth) && Mathf.Approximately(height, _lastHeight)) return;
        _lastWidth = width;
        _lastHeight = height;
        _gameObject.transform.localScale = new Vector3(width, height, 1f);
    }

    public void SetTransform(Vector3 position, Quaternion rotation)
    {
        _gameObject.transform.SetPositionAndRotation(position, rotation);
    }

    public void SetColor(Color color)
    {
        if (color == _lastColor) return;
        _lastColor = color;

        if (_instanceMaterial != null)
        {
            _instanceMaterial.color = color;
            return;
        }

        if (_block == null) return;
        _block.SetColor("_Color", color);
        _renderer.SetPropertyBlock(_block);
    }

    public void SetActive(bool active)
    {
        if (_gameObject.activeSelf != active) _gameObject.SetActive(active);
    }

    public void Destroy()
    {
        Object.Destroy(_gameObject);
        if (_mesh != null) Object.Destroy(_mesh);
        if (_instanceMaterial != null) Object.Destroy(_instanceMaterial);
    }

    private static void EnsureAssets()
    {
        if (_resolved) return;
        _resolved = true;

        _material = Materials.Shared;
        _hasColor = Materials.HasColor;
        if (_material == null) Debug.LogError("[Glissade3D] gate has no material");
    }

    private static Mesh BuildFrameMesh(float width, float height, float thickness)
    {
        var vertices = new List<Vector3>(16);
        var triangles = new List<int>(24);
        var hw = width * 0.5f;
        var hh = height * 0.5f;
        var t = Mathf.Min(thickness, Mathf.Min(hw, hh) * 0.5f);

        AddQuad(vertices, triangles, -hw, hh - t, hw, hh);
        AddQuad(vertices, triangles, -hw, -hh, hw, -hh + t);
        AddQuad(vertices, triangles, -hw, -hh, -hw + t, hh);
        AddQuad(vertices, triangles, hw - t, -hh, hw, hh);

        var mesh = new Mesh { name = "Glissade3DGateMesh" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void AddQuad(List<Vector3> vertices, List<int> triangles, float x0, float y0, float x1, float y1)
    {
        var index = vertices.Count;
        vertices.Add(new Vector3(x0, y0, 0));
        vertices.Add(new Vector3(x1, y0, 0));
        vertices.Add(new Vector3(x1, y1, 0));
        vertices.Add(new Vector3(x0, y1, 0));

        triangles.Add(index);
        triangles.Add(index + 1);
        triangles.Add(index + 2);
        triangles.Add(index);
        triangles.Add(index + 2);
        triangles.Add(index + 3);

        triangles.Add(index + 2);
        triangles.Add(index + 1);
        triangles.Add(index);
        triangles.Add(index + 3);
        triangles.Add(index + 2);
        triangles.Add(index);
    }
}
