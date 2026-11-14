using UnityEngine;

namespace Fnmw.World
{
    /// <summary>Blood rain: stretched red streaks in a box that follows the camera.</summary>
    public class BloodRain : MonoBehaviour
    {
        ParticleSystem _ps;
        public float Amount = 1f;   // 0..1, set by Atmosphere (fades out as the ship climbs)
        const float MaxRate = 2600f;

        static Material _particle;
        public static Material ParticleMaterial(Color tint)
        {
            var m = new Material(Ps1Look.Load("Rain")) { name = "fnmw_particle" };
            m.SetColor("_Tint", tint);
            return m;
        }

        public static BloodRain Create(Transform parent)
        {
            var go = new GameObject("BloodRain");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = 1.1f;
            main.startSpeed = 0f;
            main.startSize3D = false;
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.045f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 0.05f, 0.04f, 0.75f), new Color(0.45f, 0.02f, 0.02f, 0.55f));
            main.maxParticles = 6000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = MaxRate;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(44f, 1f, 44f);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(2.2f, 3.2f); vel.y = new ParticleSystem.MinMaxCurve(-24f, -19f); vel.z = new ParticleSystem.MinMaxCurve(0.4f, 1.2f);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.lengthScale = 1f; r.velocityScale = 0.035f;
            r.sharedMaterial = _particle ? _particle : _particle = ParticleMaterial(Color.white);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var rain = go.AddComponent<BloodRain>();
            rain._ps = ps;
            ps.Play();
            return rain;
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam)
            {
                var p = cam.transform.position + cam.transform.forward * 10f;
                transform.position = new Vector3(p.x, cam.transform.position.y + 11f, p.z);
            }
            var em = _ps.emission;
            em.rateOverTime = MaxRate * Mathf.Clamp01(Amount);
        }
    }
}
