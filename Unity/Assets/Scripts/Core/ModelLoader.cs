// CORE CONTRACT. Changes here must be called out in your PR description.
using UnityEngine;

namespace Fnmw.Core
{
    /// <summary>
    /// Load(name) tries Resources/Models/&lt;name&gt;, then Resources/Models/Mock/&lt;name&gt;,
    /// then returns a labelled primitive so nothing ever breaks while art is missing.
    /// </summary>
    public static class ModelLoader
    {
        public static readonly string[] KnownModels =
        {
            "player", "beast_stage1", "beast_stage2", "beast_stage3", "ship", "console",
            "rock_a", "rock_b", "ruin_pillar", "ruin_arch", "door_sealed", "mural_slab",
            "wreck_debris", "terrain_shore",
        };

        public static GameObject Load(string name, Transform parent = null)
        {
            var prefab = Resources.Load<GameObject>("Models/" + name) ?? Resources.Load<GameObject>("Models/Mock/" + name);
            GameObject go;
            if (prefab != null)
            {
                go = Object.Instantiate(prefab, parent);
                go.name = name;
            }
            else
            {
                go = Placeholder(name);
                if (parent) go.transform.SetParent(parent, false);
            }
            return go;
        }

        /// <summary>True if real or mock art exists for this name.</summary>
        public static bool HasArt(string name) =>
            Resources.Load<GameObject>("Models/" + name) != null || Resources.Load<GameObject>("Models/Mock/" + name) != null;

        static GameObject Placeholder(string name)
        {
            PrimitiveType type = PrimitiveType.Cube;
            Vector3 scale = Vector3.one;
            Color color = new Color(0.5f, 0.5f, 0.5f);
            switch (name)
            {
                case "player": type = PrimitiveType.Capsule; color = new Color(0.3f, 0.6f, 0.9f); break;
                case "beast_stage1": scale = new Vector3(1, 1.5f, 1); color = new Color(0.25f, 0.2f, 0.2f); break;
                case "beast_stage2": scale = new Vector3(1.4f, 2.2f, 1.4f); color = new Color(0.45f, 0.1f, 0.1f); break;
                case "beast_stage3": scale = new Vector3(1.8f, 3f, 1.8f); color = new Color(0.7f, 0.02f, 0.02f); break;
                case "ship": type = PrimitiveType.Capsule; scale = new Vector3(4, 6, 4); color = new Color(0.7f, 0.7f, 0.75f); break;
                case "console": scale = new Vector3(1, 1.2f, 0.6f); color = new Color(0.1f, 0.8f, 0.6f); break;
                case "rock_a": case "rock_b": type = PrimitiveType.Sphere; scale = new Vector3(2, 1.2f, 1.6f); color = new Color(0.3f, 0.25f, 0.25f); break;
                case "ruin_pillar": type = PrimitiveType.Cylinder; scale = new Vector3(0.8f, 3, 0.8f); break;
                case "ruin_arch": scale = new Vector3(4, 4, 0.6f); break;
                case "door_sealed": scale = new Vector3(2, 3, 0.3f); color = new Color(0.4f, 0.35f, 0.3f); break;
                case "mural_slab": scale = new Vector3(2, 1.5f, 0.2f); color = new Color(0.55f, 0.5f, 0.4f); break;
                case "wreck_debris": scale = new Vector3(2, 0.5f, 1); color = new Color(0.35f, 0.35f, 0.4f); break;
                case "terrain_shore": type = PrimitiveType.Plane; scale = new Vector3(10, 1, 10); color = new Color(0.25f, 0.22f, 0.22f); break;
            }

            var root = new GameObject(name + " (placeholder)");
            var body = GameObject.CreatePrimitive(type);
            body.name = "mesh";
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = scale;
            if (type != PrimitiveType.Plane) body.transform.localPosition = new Vector3(0, scale.y * (type == PrimitiveType.Cube || type == PrimitiveType.Sphere ? 0.5f : 1f), 0);
            var r = body.GetComponent<Renderer>();
            if (r != null && Application.isPlaying) r.material.color = color;

            if (type != PrimitiveType.Plane)
            {
                var label = new GameObject("label");
                label.transform.SetParent(root.transform, false);
                label.transform.localPosition = new Vector3(0, scale.y * (type == PrimitiveType.Cube || type == PrimitiveType.Sphere ? 1f : 2f) + 0.4f, 0);
                var tm = label.AddComponent<TextMesh>();
                tm.text = name;
                tm.characterSize = 0.04f;
                tm.fontSize = 48;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.color = Color.white;
                label.AddComponent<Billboard>();
            }
            return root;
        }
    }

    /// <summary>Keeps a transform facing the main camera.</summary>
    public class Billboard : MonoBehaviour
    {
        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam) transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
        }
    }
}
