using Fnmw.Core;
using UnityEngine;

namespace Fnmw.World
{
    /// <summary>
    /// Owns fog, ambient, the sky dome, the cloud deck, moonlight, lightning and the rain amount.
    /// The storm is a function of camera altitude, so the escape cutscene only has to fly the camera up:
    /// the fog thins, the rain stops, and above the cloud deck the sky turns to a calm dawn.
    /// In the Dead phase the death cutscene drives fog colour and density itself; the sky follows the fog.
    /// </summary>
    public class Atmosphere : MonoBehaviour
    {
        public static readonly Color StormFog = new Color(0.085f, 0.012f, 0.014f);
        const float StormDensity = 0.052f;
        static readonly Color StormAmbient = new Color(0.22f, 0.10f, 0.095f);
        static readonly Color MoonColor = new Color(0.78f, 0.66f, 0.6f);
        const float MoonIntensity = 0.45f;

        static readonly Color DawnTop = new Color(0.28f, 0.36f, 0.62f);
        static readonly Color DawnHorizon = new Color(1.0f, 0.70f, 0.50f);
        static readonly Color DawnBottom = new Color(0.86f, 0.62f, 0.58f);
        static readonly Color SunColor = new Color(1.0f, 0.86f, 0.62f);
        public static readonly Vector3 SunDirection = new Vector3(0.25f, 0.10f, 1f).normalized;

        Light _moon;
        Material _dome, _deck;
        Transform _domeT;
        BloodRain _rain;
        float _nextBolt = 8f, _bolt;

        public static Atmosphere Create(Transform parent, BloodRain rain)
        {
            var go = new GameObject("Atmosphere");
            go.transform.SetParent(parent, false);
            var a = go.AddComponent<Atmosphere>();
            a._rain = rain;

            var moon = new GameObject("Moonlight").AddComponent<Light>();
            moon.transform.SetParent(go.transform, false);
            moon.type = LightType.Directional;
            moon.color = MoonColor; moon.intensity = MoonIntensity;
            moon.shadows = LightShadows.None;
            moon.transform.rotation = Quaternion.Euler(38, -60, 0);
            RenderSettings.sun = moon;
            a._moon = moon;

            var dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dome.name = "SkyDome";
            Destroy(dome.GetComponent<Collider>());
            dome.transform.SetParent(go.transform, false);
            dome.transform.localScale = Vector3.one * 1800f; // drawn at the far plane by its shader, so its size only needs to clear the near plane
            a._dome = new Material(Ps1Look.Load("SkyDome")) { name = "sky" };
            var dr = dome.GetComponent<MeshRenderer>();
            dr.sharedMaterial = a._dome;
            dr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            a._domeT = dome.transform;

            var deck = GameObject.CreatePrimitive(PrimitiveType.Plane);
            deck.name = "CloudDeck";
            Destroy(deck.GetComponent<Collider>());
            deck.transform.SetParent(go.transform, false);
            deck.transform.position = new Vector3(0, WorldLayout.CloudDeckY, 0);
            deck.transform.localScale = new Vector3(500f, 1f, 500f);
            a._deck = new Material(Ps1Look.Load("CloudDeck")) { name = "clouds" };
            var cr = deck.GetComponent<MeshRenderer>();
            cr.sharedMaterial = a._deck;
            cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            RenderSettings.skybox = null;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            a.Apply(0f, 0f, 0f);
            return a;
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            float y = cam ? cam.transform.position.y : 0f;
            if (cam) _domeT.position = cam.transform.position;
            var phase = Services.State.Phase;

            // Lightning: a double flicker every so often, only while the storm owns the sky.
            bool storm = phase != GamePhase.Dead && phase != GamePhase.Escaped;
            if (storm && Time.time > _nextBolt)
            {
                _nextBolt = Time.time + Random.Range(9f, 20f);
                _bolt = 1f;
            }
            _bolt = Mathf.Max(0, _bolt - Time.deltaTime * 2.2f);
            float flash = _bolt > 0 ? (Mathf.Sin(_bolt * 40f) > 0 ? _bolt : _bolt * 0.2f) : 0f;

            float climb = Mathf.Clamp01((y - 12f) / (WorldLayout.CloudDeckY - 30f));
            float inCloud = 1f - Mathf.Clamp01(Mathf.Abs(y - WorldLayout.CloudDeckY) / 14f);
            float above = Mathf.Clamp01((y - WorldLayout.CloudDeckY) / 22f);

            if (phase == GamePhase.Dead)
            {
                // The death cutscene sets fog itself; keep the sky and the deck in step with it.
                var f = RenderSettings.fogColor;
                _dome.SetColor("_TopColor", f * 0.55f);
                _dome.SetColor("_HorizonColor", f);
                _dome.SetColor("_BottomColor", f);
                _dome.SetFloat("_SunSize", 0f);
                _deck.SetFloat("_Opacity", 0f);
                if (cam) cam.backgroundColor = f;
                _rain.Amount = 1f;
                return;
            }
            Apply(climb, inCloud, above, flash);
            if (cam) cam.backgroundColor = RenderSettings.fogColor;
        }

        void Apply(float climb, float inCloud, float above, float flash = 0f)
        {
            // Climbing out of the storm: fog thins and goes from blood-black to a bruised rose.
            Color fog = Color.Lerp(StormFog, new Color(0.30f, 0.12f, 0.14f), climb);
            float density = Mathf.Lerp(StormDensity, 0.0035f, Mathf.SmoothStep(0, 1, climb));
            // Inside the deck: white-out. Above: soft warm haze.
            fog = Color.Lerp(fog, new Color(0.95f, 0.82f, 0.80f), inCloud);
            density = Mathf.Lerp(density, 0.05f, inCloud * inCloud);
            fog = Color.Lerp(fog, DawnHorizon * 0.95f, above);
            density = Mathf.Lerp(density, 0.0011f, above);
            fog += new Color(0.25f, 0.18f, 0.2f) * flash;

            RenderSettings.fogColor = fog;
            RenderSettings.fogDensity = density;
            Color amb = Color.Lerp(StormAmbient, new Color(0.62f, 0.5f, 0.52f), above) + new Color(0.3f, 0.25f, 0.3f) * flash;
            RenderSettings.ambientLight = amb;
            Shader.SetGlobalColor("_FnmwAmbient", amb);

            _moon.color = Color.Lerp(MoonColor, SunColor, above);
            _moon.intensity = Mathf.Lerp(MoonIntensity, 1.25f, above) + flash * 2.2f;
            var stormRot = Quaternion.Euler(38, -60, 0);
            var sunRot = Quaternion.LookRotation(-SunDirection);
            _moon.transform.rotation = Quaternion.Slerp(stormRot, sunRot, above);

            _dome.SetColor("_TopColor", Color.Lerp(new Color(0.004f, 0f, 0.002f) + new Color(0.2f, 0.16f, 0.2f) * flash, DawnTop, above));
            _dome.SetColor("_HorizonColor", Color.Lerp(fog, DawnHorizon, above));
            _dome.SetColor("_BottomColor", Color.Lerp(fog, DawnBottom, above));
            _dome.SetColor("_SunColor", SunColor);
            _dome.SetVector("_SunDir", SunDirection);
            _dome.SetFloat("_SunSize", Mathf.Lerp(0f, 0.012f, above));

            _deck.SetFloat("_Opacity", Mathf.Clamp01(climb * 1.5f));
            _deck.SetColor("_LitColor", Color.Lerp(new Color(0.35f, 0.2f, 0.22f), new Color(1f, 0.88f, 0.8f), above));
            _deck.SetColor("_ShadeColor", Color.Lerp(new Color(0.15f, 0.05f, 0.07f), new Color(0.66f, 0.52f, 0.62f), above));

            if (_rain) _rain.Amount = 1f - Mathf.Clamp01(climb * 1.6f);
        }
    }
}
