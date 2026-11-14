using Fnmw.Core;
using UnityEngine;

namespace Fnmw.Game
{
    /// <summary>Game thread entry point. Spawns the player and the phase director.</summary>
    public static class GameModule
    {
        public static void Install()
        {
            var player = new GameObject("Player");
            player.transform.position = new Vector3(0, 0, 0);
            player.AddComponent<PlayerController>();
            new GameObject("GameDirector").AddComponent<GameDirector>();
        }
    }
}
