using UnityEngine;

namespace Fnmw.Game.Debrief
{
    /// <summary>Hosts the RunDebriefer for the life of the scene; a restart reloads the scene and makes a new one.</summary>
    public sealed class RunDebrief : MonoBehaviour
    {
        public RunDebriefer Debriefer { get; } = new RunDebriefer();

        void Awake()
        {
            Debriefer.Attach();
            GameDirector.Restarting += Debriefer.Cancel;
        }

        void OnDestroy()
        {
            GameDirector.Restarting -= Debriefer.Cancel;
            Debriefer.Detach();
        }
    }
}
