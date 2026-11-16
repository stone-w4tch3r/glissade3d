using UnityEngine;
using UnityEngine.Rendering;

namespace Glissade3D;

/// <summary>
/// Shared unlit material for gates and the route marker. Unlit keeps the shapes
/// readable through cockpit glass, fog and terrain haze, which is the whole point
/// of this mod's rendering choice.
/// </summary>
public static class Materials
{
    private static Material _shared;
    private static Material _translucent;
    private static bool _resolved;

    public static string ShaderName { get; private set; } = "none";
    public static bool HasColor { get; private set; }

    public static Material Shared
    {
        get
        {
            Ensure();
            return _shared;
        }
    }

    public static Material Translucent
    {
        get
        {
            EnsureTranslucent();
            return _translucent;
        }
    }

    public static Material CreateInstance()
    {
        return CreateInstance(Shared);
    }

    public static Material CreateInstance(Material shared)
    {
        return shared == null ? null : new Material(shared);
    }

    private static void Ensure()
    {
        if (_resolved) return;
        _resolved = true;

        var candidates = new[] { "KSP/Unlit", "KSP/Alpha/Translucent", "Sprites/Default", "Particles/Additive", "UI/Default" };
        foreach (var name in candidates)
        {
            var shader = Shader.Find(name);
            if (shader == null) continue;
            _shared = new Material(shader) { name = "Glissade3DUnlit" };
            ShaderName = shader.name;
            break;
        }

        if (_shared == null)
        {
            Debug.LogError("[Glissade3D] no usable shader found");
            return;
        }

        HasColor = _shared.HasProperty("_Color");
        if (HasColor) _shared.SetColor("_Color", Color.white);
        Debug.Log($"[Glissade3D] shader={ShaderName} colorProperty={HasColor}");
    }

    private static void EnsureTranslucent()
    {
        if (_translucent != null) return;

        var shader = Shader.Find("KSP/Alpha/Translucent");
        if (shader == null) shader = Shader.Find("Legacy Shaders/Transparent/Diffuse");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            Debug.LogWarning("[Glissade3D] no translucent shader found, marker stays opaque");
            return;
        }

        _translucent = new Material(shader) { name = "Glissade3DTranslucent", renderQueue = 3000 };
        if (_translucent.HasProperty("_Color")) _translucent.SetColor("_Color", new Color(1f, 1f, 1f, 0.55f));
    }

    public static void Configure(MeshRenderer renderer)
    {
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }
}
