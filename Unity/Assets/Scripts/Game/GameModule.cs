using Fnmw.Core;
using UnityEngine;

namespace Fnmw.Game
{
    /// <summary>Game thread entry point. Spawns the player, the phase director and the endings.</summary>
    public static class GameModule
    {
        public static void Install()
        {
            DemoMode.ReadLaunchFlags();
            DemoMode.Install();
            var player = new GameObject("Player");
            player.AddComponent<PlayerController>();
            var director = new GameObject("GameDirector");
            director.AddComponent<GameDirector>();
            director.AddComponent<DevKeys>();
        }
    }
}
