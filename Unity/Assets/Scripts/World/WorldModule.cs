using Fnmw.Core;
using UnityEngine;

namespace Fnmw.World
{
    /// <summary>World thread entry point. Builds the level from code. Placeholder: grey ground, red fog, one console, a ship.</summary>
    public static class WorldModule
    {
        public static void Install()
        {
            var root = new GameObject("World");

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.06f;
            RenderSettings.fogColor = new Color(0.35f, 0.02f, 0.02f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.25f, 0.08f, 0.08f);

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.55f, 0.5f);
            sun.intensity = 0.6f;
            sun.transform.SetParent(root.transform);
            sun.transform.rotation = Quaternion.Euler(35, -30, 0);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(root.transform);
            ground.transform.localScale = new Vector3(20, 1, 20);
            ground.GetComponent<Renderer>().material.color = new Color(0.35f, 0.33f, 0.33f);

            var console = ModelLoader.Load("console", root.transform);
            console.transform.position = new Vector3(0, 0, 8);
            var it = console.AddComponent<Interactable>();
            it.Type = Interactable.Kind.Console;
            it.Index = 0;

            var ship = ModelLoader.Load("ship", root.transform);
            ship.transform.position = new Vector3(10, 0, 18);

            var rng = new System.Random(1234);
            for (int i = 0; i < 12; i++)
            {
                var rock = ModelLoader.Load(i % 2 == 0 ? "rock_a" : "rock_b", root.transform);
                rock.transform.position = new Vector3(rng.Next(-30, 30), 0, rng.Next(-30, 30));
            }
        }
    }
}
