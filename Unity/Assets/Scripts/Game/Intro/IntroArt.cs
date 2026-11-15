using System.Collections.Generic;
using UnityEngine;

namespace Fnmw.Game.Intro
{
    /// <summary>
    /// Everything the opening cutscene draws or plays is generated here at runtime: materials on the
    /// Fnmw/IntroUnlit shader, procedural textures, a few meshes and the synthesized sound bed.
    /// Every created object is tracked so <see cref="Dispose"/> frees it when the intro ends.
    /// </summary>
    public sealed class IntroArt
    {
        public enum Blend { Opaque, Additive, Alpha }

        readonly List<Object> _owned = new List<Object>();
        Shader _shader;

        public Texture2D Swirl, Disk, Halo, Tunnel, Dot, Planet;
        public AudioClip Drone, Hiss, Alarm, Impact;

        public bool Valid => _shader;

        public IntroArt()
        {
            _shader = Resources.Load<Shader>("Game/Intro/IntroUnlit");
            if (!_shader) _shader = Shader.Find("Fnmw/IntroUnlit");
            if (!_shader) { Debug.LogWarning("[Intro] Fnmw/IntroUnlit shader missing; the intro will be skipped"); return; }
            Swirl = Own(MakeSwirl(256));
            Disk = Own(MakeDisk(512, 64));
            Halo = Own(MakeHalo(128));
            Tunnel = Own(MakeTunnel(128, 256));
            Dot = Own(MakeDot(32));
            Planet = Own(MakePlanet(512, 256));
            Drone = Own(MakeDrone());
            Hiss = Own(MakeHiss());
            Alarm = Own(MakeAlarm());
            Impact = Own(MakeImpact());
        }

        public T Own<T>(T o) where T : Object { if (o) _owned.Add(o); return o; }

        public void Dispose()
        {
            foreach (var o in _owned) if (o) Object.Destroy(o);
            _owned.Clear();
        }

        // ------------------------------------------------------------------ materials

        public Material Mat(Color color, Blend blend = Blend.Opaque, Texture tex = null, bool vcolor = false, bool twoSided = false, int queueOffset = 0)
        {
            var m = Own(new Material(_shader) { name = "intro" });
            m.SetColor("_Color", color);
            if (tex) m.SetTexture("_MainTex", tex);
            m.SetFloat("_VColor", vcolor ? 1f : 0f);
            switch (blend)
            {
                case Blend.Opaque:
                    m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
                    m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
                    m.SetFloat("_ZWrite", 1f);
                    m.renderQueue = 2000 + queueOffset;
                    break;
                case Blend.Additive:
                    m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
                    m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
                    m.SetFloat("_ZWrite", 0f);
                    m.renderQueue = 3000 + queueOffset;
                    break;
                case Blend.Alpha:
                    m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    m.SetFloat("_ZWrite", 0f);
                    m.renderQueue = 3000 + queueOffset;
                    break;
            }
            m.SetFloat("_Cull", twoSided ? 0f : 2f);
            return m;
        }

        public static void Rim(Material m, Color c, float strength, float power = 3f)
        {
            c.a = strength;
            m.SetColor("_RimColor", c);
            m.SetFloat("_RimPower", power);
        }

        // ------------------------------------------------------------------ objects

        /// <summary>A Unity primitive with its collider removed and our material on it.</summary>
        public static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material m, Quaternion? rot = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col) Object.Destroy(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        public GameObject MeshObject(string name, Transform parent, Mesh mesh, Material m)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = Own(mesh);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        // ------------------------------------------------------------------ meshes

        /// <summary>Flat ring in the XZ plane. u runs around (tiled), v from inner (0) to outer (1) edge.
        /// Vertex colour brightens the side toward +X, so the disk looks beamed toward the viewer on that side.</summary>
        public static Mesh Annulus(int seg, float rIn, float rOut, float uTiles, float beaming)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var col = new List<Color>(); var n = new List<Vector3>();
            var tri = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                float b = Mathf.Max(0.15f, 1f + beaming * Mathf.Cos(a));
                var c = new Color(b, b, b, 1);
                v.Add(dir * rIn); uv.Add(new Vector2(i / (float)seg * uTiles, 0)); col.Add(c); n.Add(Vector3.up);
                v.Add(dir * rOut); uv.Add(new Vector2(i / (float)seg * uTiles, 1)); col.Add(c); n.Add(Vector3.up);
                if (i < seg)
                {
                    int k = i * 2;
                    tri.AddRange(new[] { k, k + 1, k + 2, k + 1, k + 3, k + 2 });
                }
            }
            var m = new Mesh { name = "annulus" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetColors(col); m.SetNormals(n); m.SetTriangles(tri, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Open tube along +Z from z=0 to z=length, normals pointing in. u around, v along.</summary>
        public static Mesh Tube(int seg, int rings, float radius, float length, float uTiles, float vTiles)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var n = new List<Vector3>(); var tri = new List<int>();
            for (int r = 0; r <= rings; r++)
            {
                float z = r / (float)rings * length;
                for (int i = 0; i <= seg; i++)
                {
                    float a = i / (float)seg * Mathf.PI * 2f;
                    var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                    v.Add(d * radius + Vector3.forward * z);
                    n.Add(-d);
                    uv.Add(new Vector2(i / (float)seg * uTiles, r / (float)rings * vTiles));
                }
            }
            int row = seg + 1;
            for (int r = 0; r < rings; r++)
                for (int i = 0; i < seg; i++)
                {
                    int a = r * row + i, b = a + 1, c = a + row, d = c + 1;
                    tri.AddRange(new[] { a, b, c, b, d, c });
                }
            var m = new Mesh { name = "tube" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetNormals(n); m.SetTriangles(tri, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Small quads scattered on a sphere, each facing its centre: the star field.</summary>
        public static Mesh Stars(int count, float radius, int seed)
        {
            var rng = new System.Random(seed);
            float R() => (float)rng.NextDouble();
            var v = new List<Vector3>(count * 4); var uv = new List<Vector2>(count * 4); var col = new List<Color>(count * 4);
            var tri = new List<int>(count * 6);
            for (int s = 0; s < count; s++)
            {
                float z = R() * 2f - 1f, a = R() * Mathf.PI * 2f, rr = Mathf.Sqrt(1f - z * z);
                var dir = new Vector3(rr * Mathf.Cos(a), z, rr * Mathf.Sin(a));
                var c = dir * radius;
                var right = Vector3.Cross(dir, Mathf.Abs(dir.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
                var up = Vector3.Cross(right, dir);
                float size = Mathf.Lerp(14f, 34f, R() * R()) * radius / 4000f; // big enough to survive the 240-line PS1 target
                if (R() < 0.04f) size *= 2.2f;
                float b = Mathf.Lerp(0.25f, 1f, R() * R());
                float hue = R();
                var tint = hue < 0.2f ? new Color(1f, 0.72f, 0.5f) : hue < 0.45f ? new Color(0.7f, 0.8f, 1f) : Color.white;
                var col4 = tint * b; col4.a = 1;
                int k = v.Count;
                v.Add(c + (-right - up) * size); v.Add(c + (right - up) * size); v.Add(c + (right + up) * size); v.Add(c + (-right + up) * size);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
                for (int q = 0; q < 4; q++) col.Add(col4);
                tri.AddRange(new[] { k, k + 2, k + 1, k, k + 3, k + 2 });
            }
            var m = new Mesh { name = "stars", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetColors(col); m.SetTriangles(tri, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        // ------------------------------------------------------------------ textures

        static Texture2D NewTex(int w, int h, TextureWrapMode wrap, string name)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = wrap, filterMode = FilterMode.Bilinear, name = name };
        }

        /// <summary>Wormhole mouth: a twisting bright rim around a dim red interior that hints at the far side.</summary>
        static Texture2D MakeSwirl(int s)
        {
            var t = NewTex(s, s, TextureWrapMode.Clamp, "intro_swirl");
            var px = new Color[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = (x + 0.5f) / s * 2f - 1f, dy = (y + 0.5f) / s * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy), a = Mathf.Atan2(dy, dx);
                    float arms = 0.5f + 0.5f * Mathf.Sin(a * 4f + r * 16f + Mathf.PerlinNoise(r * 6f, a * 2f + 3f) * 4f);
                    float rim = Mathf.Exp(-Mathf.Pow((r - 0.62f) / 0.12f, 2f));
                    float outer = Mathf.Clamp01(1f - r) * Mathf.Clamp01((r - 0.45f) * 3f);
                    float inner = Mathf.Clamp01(1f - r / 0.55f);
                    var c = new Color(0.55f, 0.75f, 1f) * (rim * (0.6f + 0.8f * arms)) + new Color(0.35f, 0.25f, 0.9f) * (outer * arms * 0.7f)
                            + new Color(0.32f, 0.03f, 0.02f) * (inner * inner * (0.6f + 0.4f * arms));
                    if (r > 1f) c = Color.black;
                    c.a = 1f;
                    px[y * s + x] = c;
                }
            t.SetPixels(px); t.Apply(true, true);
            return t;
        }

        /// <summary>Accretion disk strip: u around the hole, v inner (white-hot) to outer (deep red), streaked.</summary>
        static Texture2D MakeDisk(int w, int h)
        {
            var t = NewTex(w, h, TextureWrapMode.Repeat, "intro_disk");
            t.wrapModeV = TextureWrapMode.Clamp;
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = x / (float)w, v = y / (float)(h - 1);
                    float streak = Mathf.PerlinNoise(u * 60f, v * 7f) * 0.7f + Mathf.PerlinNoise(u * 18f + 4f, v * 2.5f) * 0.6f;
                    // Seamless in u: blend with the wrapped-around noise near the seam.
                    float seam = Mathf.PerlinNoise((u - 1f) * 60f, v * 7f) * 0.7f + Mathf.PerlinNoise((u - 1f) * 18f + 4f, v * 2.5f) * 0.6f;
                    streak = Mathf.Lerp(streak, seam, Mathf.Clamp01((u - 0.9f) / 0.1f));
                    Color hot = Color.Lerp(new Color(1f, 0.95f, 0.85f), new Color(1f, 0.55f, 0.18f), Mathf.Clamp01(v * 2f));
                    hot = Color.Lerp(hot, new Color(0.5f, 0.07f, 0.04f), Mathf.Clamp01(v * 1.6f - 0.6f));
                    float fall = Mathf.Clamp01(v * 8f) * Mathf.Pow(1f - v, 1.6f);
                    var c = hot * fall * (0.45f + streak * 0.9f) * 1.4f;
                    c.a = 1;
                    px[y * w + x] = c;
                }
            t.SetPixels(px); t.Apply(true, true);
            return t;
        }

        /// <summary>Thin bright photon ring at half radius with a soft outer glow (lensed light around the shadow).</summary>
        static Texture2D MakeHalo(int s)
        {
            var t = NewTex(s, s, TextureWrapMode.Clamp, "intro_halo");
            var px = new Color[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = (x + 0.5f) / s * 2f - 1f, dy = (y + 0.5f) / s * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float ring = Mathf.Exp(-Mathf.Pow((r - 0.5f) / 0.025f, 2f));
                    float glow = r > 0.5f ? Mathf.Exp(-(r - 0.5f) * 7f) * 0.45f : Mathf.Exp(-(0.5f - r) * 30f) * 0.3f;
                    float edge = Mathf.Clamp01((1f - r) * 6f);
                    var c = new Color(1f, 0.86f, 0.7f) * (ring + glow) * edge;
                    c.a = 1;
                    px[y * s + x] = c;
                }
            t.SetPixels(px); t.Apply(true, true);
            return t;
        }

        /// <summary>Tunnel wall: long streaks along v with darker bands, greyscale (tinted by the material).</summary>
        static Texture2D MakeTunnel(int w, int h)
        {
            var t = NewTex(w, h, TextureWrapMode.Repeat, "intro_tunnel");
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = x / (float)w, v = y / (float)h;
                    // Periodic in both axes: sample noise on circles so the texture tiles.
                    float cu = Mathf.Cos(u * Mathf.PI * 2f), su = Mathf.Sin(u * Mathf.PI * 2f);
                    float cv = Mathf.Cos(v * Mathf.PI * 2f), sv = Mathf.Sin(v * Mathf.PI * 2f);
                    float streak = Mathf.PerlinNoise(cu * 9f + 20f, su * 9f + 20f);
                    float drift = Mathf.PerlinNoise(cv * 1.5f + cu * 2f + 50f, sv * 1.5f + su * 2f + 50f);
                    float bands = 0.5f + 0.5f * Mathf.Sin(v * Mathf.PI * 2f * 6f + drift * 6f);
                    float g = Mathf.Pow(streak, 2.2f) * 1.8f * (0.35f + 0.65f * bands) + drift * 0.15f;
                    px[y * w + x] = new Color(g, g, g, 1);
                }
            t.SetPixels(px); t.Apply(true, true);
            return t;
        }

        static Texture2D MakeDot(int s)
        {
            var t = NewTex(s, s, TextureWrapMode.Clamp, "intro_dot");
            var px = new Color[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = (x + 0.5f) / s * 2f - 1f, dy = (y + 0.5f) / s * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - r); a = a * a * (3f - 2f * a);
                    px[y * s + x] = new Color(a, a, a, a);
                }
            t.SetPixels(px); t.Apply(true, true);
            return t;
        }

        /// <summary>TrES-2b: an almost lightless world, charcoal bands with faint ember-red seams.</summary>
        static Texture2D MakePlanet(int w, int h)
        {
            var t = NewTex(w, h, TextureWrapMode.Repeat, "intro_planet");
            t.wrapModeV = TextureWrapMode.Clamp;
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = x / (float)w, v = y / (float)h;
                    float cu = Mathf.Cos(u * Mathf.PI * 2f), su = Mathf.Sin(u * Mathf.PI * 2f);
                    float warp = Mathf.PerlinNoise(cu * 2f + 7f, su * 2f + v * 6f) * 0.12f;
                    float band = 0.5f + 0.5f * Mathf.Sin((v + warp) * Mathf.PI * 22f);
                    float fine = Mathf.PerlinNoise(cu * 6f + 30f, su * 6f + v * 30f);
                    float g = 0.018f + 0.03f * band + 0.02f * fine;
                    float seam = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(band - 0.5f) * 8f), 3f) * fine;
                    px[y * w + x] = new Color(g + seam * 0.22f, g * 0.75f + seam * 0.03f, g * 0.75f, 1);
                }
            t.SetPixels(px); t.Apply(true, true);
            return t;
        }

        // ------------------------------------------------------------------ sound

        const int Rate = 22050;

        static AudioClip Clip(string name, float[] data)
        {
            var c = AudioClip.Create(name, data.Length, 1, Rate, false);
            c.SetData(data, 0);
            return c;
        }

        /// <summary>Crossfade the tail into the head so the clip loops without a click.</summary>
        static float[] Loopable(float[] raw, int fade)
        {
            int n = raw.Length - fade;
            var o = new float[n];
            for (int i = 0; i < n; i++) o[i] = raw[i];
            for (int i = 0; i < fade; i++)
            {
                float k = i / (float)fade;
                o[i] = raw[n + i] * (1f - k) + raw[i] * k;
            }
            return o;
        }

        /// <summary>Low hull drone: two slow-beating sub tones over rumbling brown noise.</summary>
        static AudioClip MakeDrone()
        {
            int fade = Rate / 2, n = Rate * 8 + fade;
            var d = new float[n];
            var rng = new System.Random(11);
            float brown = 0f, lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                brown = Mathf.Clamp(brown + ((float)rng.NextDouble() * 2f - 1f) * 0.06f, -1f, 1f) * 0.995f;
                lp += (brown - lp) * 0.05f;
                float tone = Mathf.Sin(2f * Mathf.PI * 41f * t) * 0.35f + Mathf.Sin(2f * Mathf.PI * 41.6f * t) * 0.3f
                           + Mathf.Sin(2f * Mathf.PI * 61.7f * t) * 0.12f * (0.5f + 0.5f * Mathf.Sin(t * 0.8f));
                d[i] = tone * 0.5f + lp * 1.6f;
            }
            return Clip("intro_drone", Loopable(d, fade));
        }

        /// <summary>Airy rushing noise for the transit and the atmospheric entry.</summary>
        static AudioClip MakeHiss()
        {
            int fade = Rate / 3, n = Rate * 4 + fade;
            var d = new float[n];
            var rng = new System.Random(23);
            float prev = 0f, lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float w = (float)rng.NextDouble() * 2f - 1f;
                float hp = w - prev; prev = w;            // tilt toward the highs
                lp += (hp - lp) * 0.35f;                  // but not harsh
                float t = i / (float)Rate;
                d[i] = lp * 0.35f * (0.75f + 0.25f * Mathf.Sin(t * 5.3f));
            }
            return Clip("intro_hiss", Loopable(d, fade));
        }

        /// <summary>A soft two-tone cockpit warning, one second per cycle.</summary>
        static AudioClip MakeAlarm()
        {
            int n = Rate;
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float f = t < 0.5f ? 740f : 554f;
                float local = t < 0.5f ? t : t - 0.5f;
                float env = Mathf.Clamp01(local * 60f) * Mathf.Clamp01((0.42f - local) * 30f);
                float s = Mathf.Sin(2f * Mathf.PI * f * t);
                d[i] = (s + 0.25f * Mathf.Sign(s)) * 0.22f * env;
            }
            return Clip("intro_alarm", d);
        }

        /// <summary>Impact: a sub thump and a burst of debris noise.</summary>
        static AudioClip MakeImpact()
        {
            int n = Rate * 3;
            var d = new float[n];
            var rng = new System.Random(37);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float thump = Mathf.Sin(2f * Mathf.PI * (52f - 30f * Mathf.Clamp01(t * 2f)) * t) * Mathf.Exp(-t * 3.2f);
                float w = (float)rng.NextDouble() * 2f - 1f;
                lp += (w - lp) * Mathf.Lerp(0.6f, 0.04f, Mathf.Clamp01(t * 1.5f));
                float crack = lp * Mathf.Exp(-t * 2.2f) * 1.4f;
                d[i] = Mathf.Clamp(thump * 0.9f + crack, -1f, 1f);
            }
            return Clip("intro_impact", d);
        }
    }
}
