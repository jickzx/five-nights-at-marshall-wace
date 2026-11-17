// CORE CONTRACT. Changes here must be called out in your PR description.
using UnityEngine;

namespace Fnmw.Core
{
    /// <summary>
    /// Put on anything the player can press E at. World spawns these; the Game thread's
    /// player controller finds the nearest one in range and calls Interact().
    /// </summary>
    public class Interactable : MonoBehaviour
    {
        public enum Kind { Console, Ship, Other }
        public Kind Type = Kind.Console;
        public int Index;            // console index, raised with ConsoleActivated
        public float Range = 2.5f;
        public string Prompt = "Press E to use console";
        public bool Enabled = true;

        public static readonly System.Collections.Generic.List<Interactable> All = new System.Collections.Generic.List<Interactable>();
        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        /// <summary>
        /// Where the player should be heading: the one console the director has switched on (lowest index if
        /// several), or the ship in the final chase. Null when there is nothing to walk to. The visor's
        /// objective line reads this.
        /// </summary>
        public static Interactable CurrentObjective(GameState s)
        {
            var want = s != null && s.Phase == GamePhase.Chase ? Kind.Ship : Kind.Console;
            Interactable best = null;
            for (int i = 0; i < All.Count; i++)
            {
                var it = All[i];
                if (it && it.Enabled && it.Type == want && (best == null || it.Index < best.Index)) best = it;
            }
            return best;
        }

        public void Interact()
        {
            if (!Enabled) return;
            if (Type == Kind.Console) GameEvents.RaiseConsoleActivated(Index);
        }
    }
}
