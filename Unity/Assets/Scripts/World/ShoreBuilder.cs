using System.Collections.Generic;
using Fnmw.Core;
using UnityEngine;

namespace Fnmw.World
{
    /// <summary>Builds the shore: ground, cliffs, invisible bounds, the crash site, ruins, consoles and the ship.</summary>
    public static class ShoreBuilder
    {
        static readonly Color Rock = new Color(0.17f, 0.13f, 0.12f);
        static readonly Color Rust = new Color(0.36f, 0.16f, 0.09f);
        static readonly Color Bone = new Color(0.62f, 0.56f, 0.47f);
        static readonly Color Coal = new Color(0.08f, 0.07f, 0.07f);

        public static void Build(Transform root, out List<Interactable> consoles, out Interactable ship)
        {
            BuildGround(root);
            BuildBounds(root);
            var rng = new System.Random(7177);
            BuildCrashSite(root, rng);
            BuildRuins(root);
            Scatter(root, rng);
            consoles = BuildConsoles(root);
            ship = BuildShip(root);
            var spawn = new GameObject("PlayerSpawn") { tag = "Respawn" };
            spawn.transform.SetParent(root, false);
            spawn.transform.position = WorldLayout.OnGround(WorldLayout.PlayerSpawn);
            spawn.transform.rotation = Quaternion.Euler(0, WorldLayout.PlayerSpawnYaw, 0);
        }

        // ---------------------------------------------------------------- ground

        static void BuildGround(Transform root)
        {
            bool art = ModelLoader.HasArt("terrain_shore");
            int tiles = Mathf.CeilToInt((WorldLayout.MaxX - WorldLayout.MinX + 40f) / WorldLayout.TileSize);
            float x0 = -WorldLayout.TileSize * (tiles / 2);
            var ground = new GameObject("Ground");
            ground.transform.SetParent(root, false);
            var groundMat = Ps1Look.Lit(new Color(0.24f, 0.13f, 0.1f));
            for (int t = 0; t <= tiles; t++)
            {
                float cx = x0 + t * WorldLayout.TileSize;
                if (art)
                {
                    var tile = ModelLoader.Load("terrain_shore", ground.transform);
                    tile.transform.position = new Vector3(cx, 0, 0);
                    Ps1Look.Apply(tile);
                    // The art mixes rust, coal and iron per face; pull them together so the ground reads as one surface.
                    foreach (var r in tile.GetComponentsInChildren<Renderer>())
                    {
                        var mats = r.materials;
                        foreach (var m in mats) m.SetColor("_BaseColor", Color.Lerp(m.GetColor("_BaseColor"), new Color(0.2f, 0.11f, 0.08f), 0.62f));
                        r.materials = mats;
                    }
                }
                else
                {
                    // Placeholder art is a flat plane, which the rising sea would drown at once:
                    // draw the same profile the art uses instead.
                    var go = HeightfieldObject("shore_tile", ground.transform, cx - 10, cx + 10, -10, 10, 12, 12, groundMat, true, false);
                    go.name = "terrain_shore (procedural)";
                }
            }
            // One continuous collider for the walkable strip, exact for both art and placeholder.
            HeightfieldObject("shore_collider", ground.transform, x0 - 10, x0 + tiles * WorldLayout.TileSize + 10, -10, 10, tiles * 12 + 12, 12, null, false, true);

            var cliffMat = Ps1Look.Lit(Rock);
            HeightfieldObject("cliffs", ground.transform, -260, 260, 10, 60, 104, 25, cliffMat, true, true);
            HeightfieldObject("seabed", ground.transform, -320, 320, -330, -10, 64, 32, Ps1Look.Lit(new Color(0.06f, 0.02f, 0.02f)), true, false);
            HeightfieldObject("flank_w", ground.transform, -260, x0 - 10, -10, 10, 20, 4, groundMat, true, false);
            HeightfieldObject("flank_e", ground.transform, x0 + tiles * WorldLayout.TileSize + 10, 260, -10, 10, 20, 4, groundMat, true, false);
        }

        static GameObject HeightfieldObject(string name, Transform parent, float xa, float xb, float za, float zb, int nx, int nz,
            Material mat, bool render, bool collide)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var verts = new Vector3[(nx + 1) * (nz + 1)];
            var cols = new Color[verts.Length];
            for (int j = 0; j <= nz; j++)
            for (int i = 0; i <= nx; i++)
            {
                float x = Mathf.Lerp(xa, xb, i / (float)nx), z = Mathf.Lerp(za, zb, j / (float)nz);
                verts[j * (nx + 1) + i] = new Vector3(x, WorldLayout.Height(x, z), z);
            }
            var tris = new int[nx * nz * 6];
            int k = 0;
            for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
                if (((i + j) & 1) == 0) { tris[k++] = a; tris[k++] = c; tris[k++] = d; tris[k++] = a; tris[k++] = d; tris[k++] = b; }
                else { tris[k++] = a; tris[k++] = c; tris[k++] = b; tris[k++] = b; tris[k++] = c; tris[k++] = d; }
            }
            var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = verts; mesh.triangles = tris;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            if (render)
            {
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            if (collide) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }

        static void BuildBounds(Transform root)
        {
            var b = new GameObject("Bounds");
            b.transform.SetParent(root, false);
            void Wall(Vector3 c, Vector3 size)
            {
                var w = new GameObject("wall").AddComponent<BoxCollider>();
                w.transform.SetParent(b.transform, false);
                w.transform.position = c; w.size = size;
            }
            float len = WorldLayout.MaxX - WorldLayout.MinX;
            Wall(new Vector3(0, 5, WorldLayout.WallZ + 0.5f), new Vector3(len + 4, 20, 1));
            Wall(new Vector3(0, 5, WorldLayout.SeaWallZ - 0.5f), new Vector3(len + 4, 20, 1));
            Wall(new Vector3(WorldLayout.MinX - 0.5f, 5, 2), new Vector3(1, 20, 30));
            Wall(new Vector3(WorldLayout.MaxX + 0.5f, 5, 2), new Vector3(1, 20, 30));
        }

        // ---------------------------------------------------------------- props

        /// <summary>Spawn a model on the ground, convert it to the PS1 look and give it a box collider.</summary>
        public static GameObject Prop(Transform parent, string model, float x, float z, float yaw, float scale = 1f, bool collide = true, float sink = 0.05f)
        {
            var go = ModelLoader.Load(model, parent);
            go.transform.position = new Vector3(x, WorldLayout.Height(x, z) - sink * scale, z);
            go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            go.transform.localScale = Vector3.one * scale;
            if (!ModelLoader.HasArt(model)) TintPlaceholder(go, model);
            Ps1Look.Apply(go);
            if (collide) AddBoxCollider(go);
            return go;
        }

        static void TintPlaceholder(GameObject go, string model)
        {
            Color c;
            switch (model)
            {
                case "rock_a": case "rock_b": c = Rock; break;
                case "wreck_debris": c = new Color(0.3f, 0.3f, 0.32f); break;
                case "ruin_pillar": case "ruin_arch": c = Bone * 0.8f; break;
                case "door_sealed": c = Rust; break;
                case "mural_slab": c = Bone; break;
                case "console": c = new Color(0.2f, 0.2f, 0.22f); break;
                case "ship": c = new Color(0.55f, 0.5f, 0.46f); break;
                default: return;
            }
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (r.GetComponent<TextMesh>()) { r.gameObject.SetActive(false); continue; } // no floating labels in the shipped look
                r.sharedMaterial = Ps1Look.Lit(c);
                foreach (var col in r.GetComponents<Collider>()) Object.Destroy(col);
            }
        }

        public static void AddBoxCollider(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return;
            var inv = go.transform.worldToLocalMatrix;
            Bounds lb = default; bool first = true;
            foreach (var r in rs)
            {
                if (r.GetComponent<TextMesh>()) continue;
                var wb = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = wb.center + Vector3.Scale(wb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var lp = inv.MultiplyPoint3x4(corner);
                    if (first) { lb = new Bounds(lp, Vector3.zero); first = false; } else lb.Encapsulate(lp);
                }
            }
            var bc = go.AddComponent<BoxCollider>();
            bc.center = lb.center; bc.size = lb.size * 0.92f;
        }

        static void BuildCrashSite(Transform root, System.Random rng)
        {
            var site = new GameObject("CrashSite").transform;
            site.SetParent(root, false);
            // A torn trench of wreckage behind the spawn point, still burning.
            var pieces = new (float x, float z, float yaw, float s)[]
            {
                (-73f, 4.5f, 20f, 2.6f), (-71f, 7.5f, 75f, 1.8f), (-75f, 1.5f, 140f, 2.0f), (-69.5f, 1.2f, 210f, 1.2f),
                (-70.5f, 9.6f, 300f, 1.5f), (-77.5f, 6f, 10f, 2.2f), (-63f, 8.8f, 160f, 0.9f), (-61f, 0.8f, 45f, 0.8f),
            };
            foreach (var p in pieces) Prop(site, "wreck_debris", p.x, p.z, p.yaw, p.s, true, 0.25f);
            var fire = new GameObject("Fire");
            fire.transform.SetParent(site, false);
            fire.transform.position = WorldLayout.OnGround(new Vector3(-72.5f, 0, 4.8f)) + Vector3.up * 1.2f;
            var l = fire.AddComponent<Light>();
            l.type = LightType.Point; l.color = new Color(1f, 0.42f, 0.12f); l.range = 13f; l.intensity = 3f;
            fire.AddComponent<Flicker>().Base = 3f;
            Embers.Create(fire.transform);
        }

        static void BuildRuins(Transform root)
        {
            var ruins = new GameObject("Ruins").transform;
            ruins.SetParent(root, false);
            // Pillars line the old causeway; arches frame the way forward.
            float[] pillarsX = { -54f, -47f, -26f, -15f, 10f, 16f, 44f, 52f };
            for (int i = 0; i < pillarsX.Length; i++)
            {
                float x = pillarsX[i];
                Prop(ruins, "ruin_pillar", x, 0.8f + (i % 3) * 0.3f, i * 47f, 1.1f + (i % 2) * 0.35f);
                Prop(ruins, "ruin_pillar", x + 1.5f, 8.2f - (i % 2) * 0.6f, i * 83f, 1.0f + (i % 3) * 0.3f);
            }
            Prop(ruins, "ruin_arch", -42f, 4.4f, 90f, 1.25f, false);
            Prop(ruins, "ruin_arch", 22f, 4.6f, 90f, 1.35f, false);
            Prop(ruins, "ruin_arch", 47f, 5.0f, 95f, 1.2f, false);
            // A sealed door set into the cliff, part of whatever stood here before the tide.
            Prop(ruins, "door_sealed", -10f, 10.6f, 180f, 1.3f);
            Prop(ruins, "ruin_pillar", -12.6f, 10.4f, 0f, 1.4f);
            Prop(ruins, "ruin_pillar", -7.4f, 10.4f, 90f, 1.4f);
            Prop(ruins, "door_sealed", 40f, 11f, 185f, 1.1f);
            // Murals beside the first and last console: the alien script the consoles speak.
            Prop(ruins, "mural_slab", -38.6f, 3.6f, 120f, 1.2f);
            Prop(ruins, "mural_slab", -4.5f, 0.2f, 70f, 1.1f);
            Prop(ruins, "mural_slab", 29.5f, 5.4f, 110f, 1.2f);
        }

        static void Scatter(Transform root, System.Random rng)
        {
            var rocks = new GameObject("Rocks").transform;
            rocks.SetParent(root, false);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            // Boulders and spires crowd the cliff foot.
            for (float x = WorldLayout.MinX - 10; x < WorldLayout.MaxX + 10; x += R(3.5f, 7f))
            {
                bool spire = rng.NextDouble() < 0.45;
                Prop(rocks, spire ? "rock_b" : "rock_a", x, R(9.5f, 12.5f), R(0, 360), spire ? R(1.4f, 3.2f) : R(1.3f, 2.8f));
            }
            // Black teeth in the shallows.
            for (float x = WorldLayout.MinX; x < WorldLayout.MaxX; x += R(6f, 13f))
                Prop(rocks, rng.NextDouble() < 0.5 ? "rock_b" : "rock_a", x, R(-9f, -4.5f), R(0, 360), R(0.9f, 2.2f), true, 0.3f);
            // A few stones near the path to break the line, kept out of the walking lane (z 2..7).
            for (int i = 0; i < 14; i++)
            {
                float x = R(WorldLayout.MinX + 8, WorldLayout.MaxX - 14);
                float z = rng.NextDouble() < 0.5 ? R(-3f, 0.5f) : R(7.5f, 9f);
                if (NearStation(x, z)) continue;
                Prop(rocks, "rock_a", x, z, R(0, 360), R(0.5f, 1.1f));
            }
            // Wreckage strewn along the whole shore, some of it already in the blood.
            for (int i = 0; i < 12; i++)
            {
                float x = R(WorldLayout.MinX + 12, WorldLayout.MaxX - 18), z = R(-6f, 0.5f);
                if (NearStation(x, z)) continue;
                Prop(rocks, "wreck_debris", x, z, R(0, 360), R(0.7f, 1.6f), true, 0.2f);
            }
        }

        static bool NearStation(float x, float z)
        {
            foreach (var c in WorldLayout.Consoles) if (Mathf.Abs(c.x - x) < 5f && Mathf.Abs(c.z - z) < 5f) return true;
            return Mathf.Abs(WorldLayout.Ship.x - x) < 10f;
        }

        static List<Interactable> BuildConsoles(Transform root)
        {
            var list = new List<Interactable>();
            for (int i = 0; i < WorldLayout.Consoles.Length; i++)
            {
                var p = WorldLayout.Consoles[i];
                // Face the console back toward the path so its screen greets the player coming from -X.
                var go = Prop(root, "console", p.x, p.z, 105f, 1.15f); // screen (model -Z) turned toward -X and the path
                go.name = "Console " + i;
                var it = go.AddComponent<Interactable>();
                it.Type = Interactable.Kind.Console;
                it.Index = i;
                it.Range = 2.8f;
                it.Prompt = "Press E to use the console";
                go.AddComponent<ConsoleRig>();
                list.Add(it);
            }
            return list;
        }

        static Interactable BuildShip(Transform root)
        {
            var p = WorldLayout.Ship;
            var pivot = new GameObject("Ship");
            pivot.transform.SetParent(root, false);
            pivot.transform.position = WorldLayout.OnGround(p);
            pivot.transform.rotation = Quaternion.Euler(0, WorldLayout.ShipYaw, 0);
            var model = ModelLoader.Load("ship", pivot.transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            bool art = ModelLoader.HasArt("ship");
            if (!art) TintPlaceholder(model, "ship");
            Ps1Look.Apply(model);
            AddBoxCollider(model);

            var board = new GameObject("BoardingPoint") { tag = "Finish" };
            board.transform.SetParent(pivot.transform, false);
            board.transform.localPosition = art ? new Vector3(3.0f, 0, -0.6f) : new Vector3(2.8f, 0, 0);
            var it = board.AddComponent<Interactable>();
            it.Type = Interactable.Kind.Ship;
            it.Range = 3.6f;
            it.Prompt = "Press E to board the ship";
            it.Enabled = false;
            var rig = pivot.AddComponent<ShipRig>();
            rig.Model = model;
            rig.HasArt = art;
            // Clear a landing pad of rocks: they were already kept away in Scatter.
            return it;
        }
    }

    /// <summary>Firelight flicker.</summary>
    public class Flicker : MonoBehaviour
    {
        public float Base = 2f, Amount = 0.35f, Speed = 9f;
        Light _l; float _seed;
        void Awake() { _l = GetComponent<Light>(); _seed = Random.value * 100f; }
        void Update()
        {
            if (!_l) return;
            float n = Mathf.PerlinNoise(_seed, Time.time * Speed);
            _l.intensity = Base * (1f - Amount + n * Amount * 2f);
        }
    }

    /// <summary>Sparks rising off the burning wreck.</summary>
    public static class Embers
    {
        public static ParticleSystem Create(Transform parent)
        {
            var go = new GameObject("Embers");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = 2.2f; main.startSpeed = 1.6f; main.startSize = 0.07f;
            main.startColor = new Color(1f, 0.5f, 0.15f, 1f);
            main.maxParticles = 200; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.15f;
            var em = ps.emission; em.rateOverTime = 28f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 1.2f;
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.x = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f); vel.y = new ParticleSystem.MinMaxCurve(0.2f, 0.8f); vel.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.7f, 0.3f), 0), new GradientColorKey(new Color(0.8f, 0.1f, 0.05f), 1) },
                      new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = BloodRain.ParticleMaterial(new Color(1f, 1f, 1f, 1f));
            ps.Play();
            return ps;
        }
    }
}
