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

        public void Interact()
        {
            if (!Enabled) return;
            if (Type == Kind.Console) GameEvents.RaiseConsoleActivated(Index);
        }
    }
}
