using Fnmw.Core;
using UnityEngine;

namespace Fnmw
{
    /// <summary>
    /// The only object in Main.unity. Resets Core to fakes, then lets each area install itself.
    /// Order: services first (Ai, Lang, Audio, UI), then things that spawn (World, Monster, Game).
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class GameBootstrap : MonoBehaviour
    {
        void Awake()
        {
            GameEvents.ClearAll();
            Services.ResetToFakes();
            Core.Ai.AiModule.Install();
            Lang.LangModule.Install();
            Audio.AudioModule.Install();
            UI.UIModule.Install();
            World.WorldModule.Install();
            Monster.MonsterModule.Install();
            Game.GameModule.Install();
            Voice.VoiceModule.Install();   // optional voices; offline (no network at all) without a backend config
            Debug.Log("[GameBootstrap] installed all modules");
        }
    }
}
