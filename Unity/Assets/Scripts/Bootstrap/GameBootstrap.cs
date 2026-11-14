using Fnmw.Core;
using UnityEngine;

namespace Fnmw
{
    /// <summary>
    /// The only object in Main.unity. Resets Core to fakes, then lets each area install itself.
    /// Order: services first (Lang, Audio, UI), then things that spawn (World, Monster, Game).
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class GameBootstrap : MonoBehaviour
    {
        void Awake()
        {
            GameEvents.ClearAll();
            Services.ResetToFakes();
            Lang.LangModule.Install();
            Audio.AudioModule.Install();
            UI.UIModule.Install();
            World.WorldModule.Install();
            Monster.MonsterModule.Install();
            Game.GameModule.Install();
            Debug.Log("[GameBootstrap] installed all modules");
        }
    }
}
