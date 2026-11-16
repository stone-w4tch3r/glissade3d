using UnityEngine;

namespace Glissade3D;

[KSPAddon(KSPAddon.Startup.MainMenu, false)]
public sealed class Glissade3DDiagnostics : MonoBehaviour
{
    private void Start()
    {
        var version = typeof(Glissade3DDiagnostics).Assembly.GetName().Version;
        var runways = GameDatabase.Instance.GetConfigNodes("Runway");
        var unlit = Shader.Find("KSP/Unlit");
        var translucent = Shader.Find("KSP/Alpha/Translucent");

        Debug.Log($"[Glissade3D] diagnostics: version={version}, runwayNodes={runways?.Length ?? -1}, " +
                  $"unlit={(unlit != null)}, translucent={(translucent != null)}");
    }
}
