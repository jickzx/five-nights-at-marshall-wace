using System.Collections.Generic;
using UnityEngine;

namespace Fnmw.World
{
    /// <summary>
    /// Material factory and converter for the PS1 look. Everything solid is redrawn with Fnmw/PS1Lit:
    /// flat shaded, vertex snapped, fogged. Art colours, textures and emission are carried over.
    /// </summary>
    public static class Ps1Look
    {
        static Shader _lit;
        static readonly Dictionary<Material, Material> Converted = new Dictionary<Material, Material>();
        static readonly HashSet<string> StockShaders = new HashSet<string>
        {
            "Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit", "Universal Render Pipeline/Complex Lit",
            "Universal Render Pipeline/Baked Lit", "Standard", "Hidden/InternalErrorShader",
        };

        public static Shader LitShader => _lit ? _lit : _lit = Load("Ps1Lit");

        public static Shader Load(string name)
        {
            var s = Resources.Load<Shader>("World/" + name);
            if (!s) s = Shader.Find("Fnmw/" + name.Replace("Ps1", "PS1"));
            return s;
        }

        public static void ResetCache() => Converted.Clear();

        public static Material Lit(Color color, Color emission = default)
        {
            var m = new Material(LitShader) { name = "ps1" };
            m.SetColor("_BaseColor", color);
            m.SetColor("_EmissionColor", emission);
            return m;
        }

        public static bool IsStock(Material m) => m && m.shader && (StockShaders.Contains(m.shader.name) || m.shader.name.StartsWith("Universal Render Pipeline/Lit"));

        /// <summary>Convert every stock-shaded renderer under root. Labels, particles and custom shaders are left alone.</summary>
        public static void Apply(GameObject root, bool onlyStock = false)
        {
            if (!root || !LitShader) return;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true)) Convert(r, onlyStock);
        }

        public static bool Convert(Renderer r, bool onlyStock)
        {
            if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) return false;
            if (r.GetComponent<TextMesh>()) return false;
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var src = mats[i];
                if (src && src.shader && src.shader.name.StartsWith("Fnmw/")) continue;
                if (onlyStock && src && !IsStock(src)) continue;
                mats[i] = ConvertMaterial(src);
                changed = true;
            }
            if (changed)
            {
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            return changed;
        }

        static Material ConvertMaterial(Material src)
        {
            if (!src) return Lit(new Color(0.4f, 0.36f, 0.33f));
            var key = src;
            if (Converted.TryGetValue(key, out var cached) && cached) return cached;
            Color c = src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : src.HasProperty("_Color") ? src.GetColor("_Color") : Color.gray;
            Color e = Color.black;
            if (src.HasProperty("_EmissionColor") && (src.IsKeywordEnabled("_EMISSION") || src.globalIlluminationFlags != MaterialGlobalIlluminationFlags.EmissiveIsBlack))
                e = src.GetColor("_EmissionColor");
            var m = Lit(c, e);
            m.name = src.name + " (ps1)";
            Texture tex = src.HasProperty("_BaseMap") ? src.GetTexture("_BaseMap") : src.HasProperty("_MainTex") ? src.GetTexture("_MainTex") : null;
            if (tex)
            {
                tex.filterMode = FilterMode.Point;
                m.SetTexture("_BaseMap", tex);
            }
            Converted[key] = m;
            return m;
        }
    }

    /// <summary>Keeps converting stock-shaded renderers spawned later (the beast's stage swaps, the player body).</summary>
    public class Ps1Sweeper : MonoBehaviour
    {
        float _next;
        void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.5f;
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                var m = r.sharedMaterial;
                if (m && Ps1Look.IsStock(m)) Ps1Look.Convert(r, true);
            }
        }
    }
}
