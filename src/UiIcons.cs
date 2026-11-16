using System.IO;
using UnityEngine;

namespace Glissade3D;

internal static class UiIcons
{
    private static Texture2D _edit;
    private static Texture2D _copy;
    private static Texture2D _delete;

    public static Texture2D Edit
    {
        get { return Get(ref _edit, "edit"); }
    }

    public static Texture2D Copy
    {
        get { return Get(ref _copy, "copy"); }
    }

    public static Texture2D Delete
    {
        get { return Get(ref _delete, "delete"); }
    }

    private static Texture2D Get(ref Texture2D cache, string name)
    {
        if (cache != null) return cache;
        cache = GameDatabase.Instance.GetTexture("Glissade3D/Icons/" + name, false);
        if (cache != null) return cache;

        var path = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "Glissade3D", "Icons", name + ".png");
        if (!File.Exists(path)) return null;
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(File.ReadAllBytes(path))) return null;
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;
        cache = texture;
        return cache;
    }
}
