using System;
using UnityEngine;

namespace GlissadeTest;

[KSPAddon(KSPAddon.Startup.MainMenu, false)]
public sealed class TestLoader : MonoBehaviour
{
    private void Start()
    {
        if (Environment.GetEnvironmentVariable("GLISSADE_AUTOLOAD") != "1") return;

        Debug.Log("[TestLoader] autoload requested");
        var game = GamePersistence.LoadGame("Dynawing Final Approach", "scenarios", true, true);
        if (game == null)
        {
            Debug.LogError("[TestLoader] save load failed");
            return;
        }

        game.startScene = GameScenes.FLIGHT;
        HighLogic.CurrentGame = game;
        Debug.Log("[TestLoader] starting game");
        game.Start();
        Debug.Log("[TestLoader] switching to FLIGHT");
        HighLogic.LoadScene(GameScenes.FLIGHT);
    }
}
