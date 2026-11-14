using Fnmw.Core;
using UnityEngine;

namespace Fnmw.World
{
    /// <summary>
    /// World thread entry point. Builds the blood shore from code: terrain and cliffs, the crash site, ruins,
    /// consoles (Interactable) and the ship (Interactable Kind.Ship), the rising blood sea, rain, fog and sky,
    /// and the PS1 render pass. Tags: the player spawn is tagged "Respawn", the ship's boarding point "Finish".
    /// </summary>
    public static class WorldModule
    {
        public static void Install()
        {
            Ps1Look.ResetCache();
            var root = new GameObject("World");
            Shader.SetGlobalFloat("_FnmwLightSteps", 0f);
            ShoreBuilder.Build(root.transform, out _, out _);
            var sea = BloodSea.Create(root.transform);
            Services.State.SeaLevel = sea.Level;
            var rain = BloodRain.Create(root.transform);
            Atmosphere.Create(root.transform, rain);
            RetroScreen.Create(root.transform);
            root.AddComponent<Ps1Sweeper>();
        }
    }
}
