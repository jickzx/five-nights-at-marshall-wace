using UnityEngine;

namespace Fnmw.Game.Intro
{
    /// <summary>
    /// The intro's runtime-built stage, kept on its own layer far above the level so only the intro camera
    /// sees it: the survey ship (hull + cockpit), the star field, the wormhole mouth, the transit tunnel,
    /// and the TrES-2b system (the planet and the black hole that fills its sky). All local to Root, ship flies +Z.
    /// </summary>
    public sealed class IntroSet
    {
        public const int Layer = 30;
        public static readonly Vector3 Origin = new Vector3(0f, 6000f, 0f);

        // System layout, local to Root (the emergence shot looks from near the origin toward +Z).
        public static readonly Vector3 HolePos = new Vector3(520f, 520f, 2100f);
        public const float HoleRadius = 400f;
        public static readonly Vector3 PlanetPos = new Vector3(-260f, -1150f, 1500f);
        public const float PlanetRadius = 950f;
        public const float WormholeZ = 420f;

        public Transform Root, Ship, Exterior, Cockpit, CockpitEye, Stars, Wormhole, WormholeMouth, WormholeBack, Tunnel, TunnelGlow, Hole, Disk, Lens, Planet, ExitMouth;
        public Material TunnelMat, TunnelGlowMat, DiskMat, MouthMat, AlarmMat, ExitMat;
        public Material[] Screens;
        public Transform[] EngineGlows;
        public ParticleSystem Dust, Plasma, Fire, Smoke;

        readonly IntroArt _art;

        public IntroSet(IntroArt art)
        {
            _art = art;
            Root = new GameObject("IntroSet").transform;
            Root.position = Origin;
            BuildStars();
            BuildShip();
            BuildWormhole();
            BuildTunnel();
            BuildSystem();
            SetLayer(Root, Layer);
        }

        public static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform c in t) SetLayer(c, layer);
        }

        public void Destroy()
        {
            if (Root) Object.Destroy(Root.gameObject);
        }

        // ------------------------------------------------------------------ star field

        void BuildStars()
        {
            var m = _art.Mat(Color.white, IntroArt.Blend.Additive, _art.Dot, vcolor: true, twoSided: true, queueOffset: -10);
            Stars = _art.MeshObject("Stars", Root, IntroArt.Stars(2600, 5200f, 7), m).transform;
            Stars.localPosition = Vector3.zero;
        }

        // ------------------------------------------------------------------ ship

        void BuildShip()
        {
            Ship = new GameObject("Ship").transform;
            Ship.SetParent(Root, false);
            // Outside shots show the hull; the pilot's shots hide it and show the cockpit frame instead.
            Exterior = new GameObject("Exterior").transform;
            Exterior.SetParent(Ship, false);
            var hull = _art.Mat(new Color(0.16f, 0.17f, 0.2f));
            IntroArt.Rim(hull, new Color(0.55f, 0.65f, 0.85f), 0.9f, 2.5f);
            var dark = _art.Mat(new Color(0.06f, 0.06f, 0.07f));
            IntroArt.Rim(dark, new Color(0.4f, 0.45f, 0.55f), 0.6f, 2f);
            var glass = _art.Mat(new Color(0.02f, 0.03f, 0.05f));
            IntroArt.Rim(glass, new Color(0.6f, 0.75f, 1f), 1.1f, 4f);

            IntroArt.Prim(PrimitiveType.Cube, "Hull", Exterior, new Vector3(0, 0, -0.5f), new Vector3(1.7f, 1.0f, 6.2f), hull);
            IntroArt.Prim(PrimitiveType.Sphere, "Nose", Exterior, new Vector3(0, 0, 2.6f), new Vector3(1.7f, 1.0f, 3.2f), hull);
            IntroArt.Prim(PrimitiveType.Sphere, "Canopy", Exterior, new Vector3(0, 0.42f, 2.2f), new Vector3(1.05f, 0.55f, 1.6f), glass);
            IntroArt.Prim(PrimitiveType.Cube, "Wing", Exterior, new Vector3(0, -0.15f, -1.4f), new Vector3(6.2f, 0.12f, 2.2f), dark,
                Quaternion.Euler(0, 0, 0));
            IntroArt.Prim(PrimitiveType.Cube, "Fin", Exterior, new Vector3(0, 0.8f, -2.9f), new Vector3(0.12f, 1.2f, 1.6f), dark);
            EngineGlows = new Transform[2];
            var glowMat = _art.Mat(new Color(0.55f, 0.75f, 1f), IntroArt.Blend.Additive, _art.Dot, twoSided: true, queueOffset: 5);
            for (int s = 0; s < 2; s++)
            {
                float x = s == 0 ? -1.15f : 1.15f;
                IntroArt.Prim(PrimitiveType.Cylinder, "Engine", Exterior, new Vector3(x, -0.05f, -3.1f), new Vector3(0.62f, 1.1f, 0.62f), dark,
                    Quaternion.Euler(90, 0, 0));
                EngineGlows[s] = IntroArt.Prim(PrimitiveType.Quad, "EngineGlow", Exterior, new Vector3(x, -0.05f, -4.35f), Vector3.one * 1.6f, glowMat).transform;
            }
            var red = _art.Mat(new Color(1f, 0.2f, 0.15f), IntroArt.Blend.Additive, _art.Dot, twoSided: true, queueOffset: 5);
            var green = _art.Mat(new Color(0.3f, 1f, 0.45f), IntroArt.Blend.Additive, _art.Dot, twoSided: true, queueOffset: 5);
            IntroArt.Prim(PrimitiveType.Quad, "NavL", Exterior, new Vector3(-3.1f, -0.1f, -1.4f), Vector3.one * 0.5f, red);
            IntroArt.Prim(PrimitiveType.Quad, "NavR", Exterior, new Vector3(3.1f, -0.1f, -1.4f), Vector3.one * 0.5f, green);

            BuildCockpit();
            BuildShipParticles();
        }

        /// <summary>The pilot's view: window struts, a dashboard with glowing displays and a warning lamp.
        /// The hull's faces point outward, so from inside they are culled and only this frame shows.</summary>
        void BuildCockpit()
        {
            Cockpit = new GameObject("Cockpit").transform;
            Cockpit.SetParent(Ship, false);
            Cockpit.localPosition = new Vector3(0, 0.45f, 1.7f);
            CockpitEye = new GameObject("Eye").transform;
            CockpitEye.SetParent(Cockpit, false);

            var frame = _art.Mat(new Color(0.035f, 0.035f, 0.04f));
            IntroArt.Rim(frame, new Color(0.25f, 0.3f, 0.4f), 0.5f, 2f);
            IntroArt.Prim(PrimitiveType.Cube, "StrutL", Cockpit, new Vector3(-0.95f, 0.15f, 1.1f), new Vector3(0.12f, 1.6f, 0.12f), frame, Quaternion.Euler(-12, 0, 18));
            IntroArt.Prim(PrimitiveType.Cube, "StrutR", Cockpit, new Vector3(0.95f, 0.15f, 1.1f), new Vector3(0.12f, 1.6f, 0.12f), frame, Quaternion.Euler(-12, 0, -18));
            IntroArt.Prim(PrimitiveType.Cube, "Brow", Cockpit, new Vector3(0, 0.78f, 0.9f), new Vector3(2.4f, 0.22f, 0.9f), frame, Quaternion.Euler(-8, 0, 0));
            IntroArt.Prim(PrimitiveType.Cube, "Dash", Cockpit, new Vector3(0, -0.62f, 0.95f), new Vector3(2.6f, 0.5f, 0.9f), frame, Quaternion.Euler(14, 0, 0));
            IntroArt.Prim(PrimitiveType.Cube, "SideL", Cockpit, new Vector3(-1.35f, -0.1f, 0.4f), new Vector3(0.3f, 1.6f, 1.8f), frame);
            IntroArt.Prim(PrimitiveType.Cube, "SideR", Cockpit, new Vector3(1.35f, -0.1f, 0.4f), new Vector3(0.3f, 1.6f, 1.8f), frame);

            Screens = new Material[3];
            var colors = new[] { new Color(0.25f, 0.7f, 0.75f), new Color(0.85f, 0.55f, 0.2f), new Color(0.25f, 0.7f, 0.75f) };
            for (int i = 0; i < 3; i++)
            {
                Screens[i] = _art.Mat(colors[i], IntroArt.Blend.Additive, null, twoSided: true, queueOffset: 4);
                IntroArt.Prim(PrimitiveType.Quad, "Screen" + i, Cockpit, new Vector3((i - 1) * 0.6f, -0.33f, 0.62f), new Vector3(0.42f, 0.2f, 1f),
                    Screens[i], Quaternion.Euler(52, (i - 1) * -14f, 0));
            }
            AlarmMat = _art.Mat(new Color(1f, 0.12f, 0.06f), IntroArt.Blend.Additive, _art.Dot, twoSided: true, queueOffset: 6);
            IntroArt.Prim(PrimitiveType.Quad, "Alarm", Cockpit, new Vector3(0, 0.64f, 0.5f), new Vector3(0.5f, 0.25f, 1f), AlarmMat, Quaternion.Euler(-30, 0, 0));
        }

        void BuildShipParticles()
        {
            var dustMat = _art.Mat(Color.white, IntroArt.Blend.Additive, _art.Dot, vcolor: true, twoSided: true, queueOffset: 8);
            Dust = Particles("Dust", Ship, dustMat, local: true);
            {
                var main = Dust.main;
                main.startLifetime = 1.4f; main.startSpeed = 0f; main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.12f);
                main.startColor = new Color(0.55f, 0.62f, 0.75f, 1f); main.maxParticles = 600;
                var em = Dust.emission; em.rateOverTime = 260f;
                var sh = Dust.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(36f, 24f, 4f); sh.position = new Vector3(0, 0, 60f);
                var vel = Dust.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.Local;
                vel.x = new ParticleSystem.MinMaxCurve(0f, 0f); vel.y = new ParticleSystem.MinMaxCurve(0f, 0f); vel.z = new ParticleSystem.MinMaxCurve(-48f, -48f);
                var r = Dust.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.05f; r.lengthScale = 2f;
            }
            var plasmaMat = _art.Mat(Color.white, IntroArt.Blend.Additive, _art.Dot, vcolor: true, twoSided: true, queueOffset: 9);
            Plasma = Particles("Plasma", Ship, plasmaMat, local: true);
            {
                var main = Plasma.main;
                main.startLifetime = 0.45f; main.startSpeed = 0f; main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 1.6f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.45f, 0.12f, 1f), new Color(1f, 0.85f, 0.5f, 1f)); main.maxParticles = 900;
                var em = Plasma.emission; em.rateOverTime = 0f;
                var sh = Plasma.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 35f; sh.radius = 1.4f;
                sh.position = new Vector3(0, 0, 9f); sh.rotation = new Vector3(180f, 0, 0);
                var vel = Plasma.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.Local;
                vel.x = new ParticleSystem.MinMaxCurve(0f, 0f); vel.y = new ParticleSystem.MinMaxCurve(0f, 0f); vel.z = new ParticleSystem.MinMaxCurve(-30f, -30f);
                var col = Plasma.colorOverLifetime; col.enabled = true; col.color = Fading();
                var r = Plasma.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.06f; r.lengthScale = 1.5f;
            }
            var fireMat = _art.Mat(Color.white, IntroArt.Blend.Additive, _art.Dot, vcolor: true, twoSided: true, queueOffset: 10);
            Fire = Particles("Fire", Ship, fireMat, local: false);
            {
                var main = Fire.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.3f); main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 3f);
                main.startSize = new ParticleSystem.MinMaxCurve(1.6f, 4.2f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.5f, 0.15f, 1f), new Color(1f, 0.88f, 0.55f, 1f)); main.maxParticles = 1500;
                var em = Fire.emission; em.rateOverTime = 0f;
                var sh = Fire.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 1.6f;
                var col = Fire.colorOverLifetime; col.enabled = true; col.color = Fading();
                var size = Fire.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 1.6f));
            }
            var smokeMat = _art.Mat(Color.white, IntroArt.Blend.Alpha, _art.Dot, vcolor: true, twoSided: true, queueOffset: 1);
            Smoke = Particles("Smoke", Ship, smokeMat, local: false);
            {
                var main = Smoke.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 3f); main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.5f);
                main.startSize = new ParticleSystem.MinMaxCurve(3f, 6f);
                main.startColor = new Color(0.05f, 0.03f, 0.03f, 0.55f); main.maxParticles = 900;
                var em = Smoke.emission; em.rateOverTime = 0f;
                var sh = Smoke.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 1.2f;
                var col = Smoke.colorOverLifetime; col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                          new[] { new GradientAlphaKey(0.9f, 0), new GradientAlphaKey(0f, 1) });
                col.color = g;
                var size = Smoke.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.7f, 1, 2.4f));
            }
        }

        static ParticleSystem.MinMaxGradient Fading()
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(0.6f, 0.15f, 0.05f), 1) },
                      new[] { new GradientAlphaKey(1f, 0), new GradientAlphaKey(0f, 1) });
            return g;
        }

        ParticleSystem Particles(string name, Transform parent, Material m, bool local)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.simulationSpace = local ? ParticleSystemSimulationSpace.Local : ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        // ------------------------------------------------------------------ wormhole + tunnel

        void BuildWormhole()
        {
            Wormhole = new GameObject("Wormhole").transform;
            Wormhole.SetParent(Root, false);
            Wormhole.localPosition = new Vector3(0, 0, WormholeZ);
            // A black lens behind the bright mouth hides the stars that the far side would replace.
            var lens = _art.Mat(new Color(0.01f, 0f, 0.01f), IntroArt.Blend.Opaque, null, twoSided: true);
            WormholeBack = IntroArt.Prim(PrimitiveType.Quad, "Lens", Wormhole, new Vector3(0, 0, 2f), Vector3.one * 0.6f, lens).transform;
            MouthMat = _art.Mat(Color.white, IntroArt.Blend.Additive, _art.Swirl, twoSided: true, queueOffset: 20);
            WormholeMouth = IntroArt.Prim(PrimitiveType.Quad, "Mouth", Wormhole, Vector3.zero, Vector3.one, MouthMat).transform;
            Wormhole.localScale = Vector3.one * 8f;

            // The exit, shrinking behind the ship in the TrES-2b system.
            ExitMat = _art.Mat(new Color(1f, 0.6f, 0.6f), IntroArt.Blend.Additive, _art.Swirl, twoSided: true, queueOffset: 20);
            ExitMouth = IntroArt.Prim(PrimitiveType.Quad, "ExitMouth", Root, new Vector3(0, 0, -60f), Vector3.one * 40f, ExitMat).transform;
            ExitMouth.gameObject.SetActive(false);
        }

        void BuildTunnel()
        {
            Tunnel = new GameObject("Tunnel").transform;
            Tunnel.SetParent(Ship, false);
            Tunnel.localPosition = new Vector3(0, 0, -40f);
            TunnelMat = _art.Mat(new Color(0.35f, 0.5f, 1f), IntroArt.Blend.Additive, _art.Tunnel, twoSided: true, queueOffset: 15);
            TunnelMat.mainTextureScale = new Vector2(3f, 10f);
            _art.MeshObject("Wall", Tunnel, IntroArt.Tube(40, 60, 16f, 700f, 1f, 1f), TunnelMat);
            TunnelGlowMat = _art.Mat(new Color(0.8f, 0.85f, 1f), IntroArt.Blend.Additive, _art.Dot, twoSided: true, queueOffset: 14);
            TunnelGlow = IntroArt.Prim(PrimitiveType.Quad, "FarGlow", Tunnel, new Vector3(0, 0, 690f), Vector3.one * 60f, TunnelGlowMat).transform;
            Tunnel.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ TrES-2b system

        void BuildSystem()
        {
            var planetMat = _art.Mat(Color.white, IntroArt.Blend.Opaque, _art.Planet);
            IntroArt.Rim(planetMat, new Color(0.7f, 0.12f, 0.06f), 1.3f, 3.5f);
            Planet = IntroArt.Prim(PrimitiveType.Sphere, "TrES-2b", Root, PlanetPos, Vector3.one * PlanetRadius * 2f, planetMat,
                Quaternion.Euler(18f, 30f, -12f)).transform;

            Hole = new GameObject("BlackHole").transform;
            Hole.SetParent(Root, false);
            Hole.localPosition = HolePos;
            var shadow = _art.Mat(Color.black, IntroArt.Blend.Opaque, null);
            IntroArt.Prim(PrimitiveType.Sphere, "Shadow", Hole, Vector3.zero, Vector3.one * HoleRadius * 2f, shadow);
            DiskMat = _art.Mat(Color.white, IntroArt.Blend.Additive, _art.Disk, vcolor: true, twoSided: true, queueOffset: 30);
            DiskMat.mainTextureScale = new Vector2(1f, 1f);
            Disk = _art.MeshObject("AccretionDisk", Hole, IntroArt.Annulus(128, HoleRadius * 1.35f, HoleRadius * 4.2f, 2f, 0.75f), DiskMat).transform;
            Disk.localRotation = Quaternion.Euler(9f, 0f, -14f);
            var lensMat = _art.Mat(new Color(1f, 0.8f, 0.6f), IntroArt.Blend.Additive, _art.Halo, twoSided: true, queueOffset: 31);
            Lens = IntroArt.Prim(PrimitiveType.Quad, "PhotonRing", Hole, Vector3.zero, Vector3.one * HoleRadius * 4.4f, lensMat).transform;
            SetSystemVisible(false);
        }

        public void SetSystemVisible(bool on)
        {
            Planet.gameObject.SetActive(on);
            Hole.gameObject.SetActive(on);
        }

        /// <summary>Turn a quad to face the camera (Unity quads show their -Z side).</summary>
        public static void Billboard(Transform quad, Vector3 camPos)
        {
            var d = quad.position - camPos;
            if (d.sqrMagnitude > 1e-6f) quad.rotation = Quaternion.LookRotation(d);
        }
    }
}
