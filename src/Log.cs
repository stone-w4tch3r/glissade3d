using UnityEngine;

namespace Glissade3D;

/// <summary>
/// Opt-in debug logging. Off by default; enabled by the Advanced checkbox or by
/// GLISSADE_DEBUG=1. Kept separate from ordinary log lines so a normal session
/// stays readable.
/// </summary>
public static class Dbg
{
    public static bool Enabled;

    public static void Line(string message)
    {
        if (!Enabled) return;
        Debug.Log("[Glissade3D][DBG] " + message);
    }
}
