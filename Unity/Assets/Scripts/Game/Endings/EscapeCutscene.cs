using System;
using System.Collections;
using System.Collections.Generic;
using Fnmw.Core;
using UnityEngine;

namespace Fnmw.Game
{
    /// <summary>
    /// The escape ending, about 27 seconds. The hatch seals, the engines light and blow the blood flat,
    /// the ship lifts through the rain while the shore and the red sea shrink away below, punches through
    /// the cloud deck and comes out into a quiet dawn, sliding toward a low sun over a sea of cloud.
    /// The World's Atmosphere does the weather from camera altitude; this script flies the ship and camera.
    /// Uses the ship's hatch_close and takeoff takes when the art has them, animates in code otherwise.
    /// </summary>
    public class EscapeCutscene : MonoBehaviour
    {
        public const float Length = 27f;
        const float LiftStart = 7.5f, ClimbTime = 12f, CruiseAlt = 185f;
        static readonly Color Gold = new Color(1f, 0.93f, 0.8f);
        static readonly Vector3 SunDir = new Vector3(0.25f, 0.10f, 1f).normalized; // matches World's Atmosphere

        Action _onDone;
        Transform _pivot;

        public void Play(Transform shipPivot, Action onDone)
        {
            _pivot = shipPivot;
            _onDone = onDone;
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            var cam = Camera.main ? Camera.main.transform : null;
            if (!cam || !_pivot) { yield return new WaitForSeconds(1f); _onDone?.Invoke(); yield break; }
            var fade = ScreenFade.Create(transform);
            var player = Services.Player ? Services.Player.GetComponent<PlayerController>() : null;
            if (player) { player.SetFlashlight(false); player.gameObject.SetActive(false); } // aboard
            cam.SetParent(null, true);
            var camera = cam.GetComponent<Camera>();
            if (camera) camera.farClipPlane = 7000f; // the whole red sea, out to where the haze swallows it

            GameObject model = null;
            foreach (Transform c in _pivot) if (c.name.StartsWith("ship")) { model = c.gameObject; break; }
            if (!model && _pivot.childCount > 0) model = _pivot.GetChild(0).gameObject;
            foreach (var col in _pivot.GetComponentsInChildren<Collider>()) col.enabled = false;

            Vector3 basePos = _pivot.position;
            Quaternion baseRot = _pivot.rotation;
            Vector3 door = _pivot.right;           // model +X: the side hatch
            Vector3 nose = -_pivot.forward;        // model -Z: the prow
            Vector3 travel = new Vector3(SunDir.x, 0, SunDir.z).normalized;

            // Hatch.
            bool clipHatch = model && ClipPlayer.Play(model, "ship", "hatch_close");
            Transform hatch = clipHatch ? null : FindDeep(model ? model.transform : _pivot, "hatch");
            Quaternion hatchOpen = hatch ? hatch.localRotation : Quaternion.identity;

            var exhausts = new List<Renderer>();
            if (model) foreach (var r in model.GetComponentsInChildren<Renderer>(true)) if (r.name.ToLowerInvariant().Contains("exhaust")) exhausts.Add(r);
            var thrust = MakeThrustLight(_pivot);
            ParticleSystem spray = null;

            bool tookOff = false;
            float t = 0f;
            Vector3 shotA = basePos + door * 11f - nose * 4f + Vector3.up * 1.6f;
            Vector3 shotB = basePos + door * 6.5f - nose * 5f + Vector3.up * 0.7f;
            Vector3 shipCenter = Vector3.up * 2f;
            bool done = false;

            while (true)
            {
                t += Time.deltaTime;

                // ---- ship motion
                if (hatch) hatch.localRotation = Quaternion.Slerp(hatchOpen, Quaternion.identity, Ease.InOut(t / 2.6f));
                if (!tookOff && t > 3f)
                {
                    tookOff = true;
                    foreach (var r in exhausts) r.enabled = true;
                    if (model) ClipPlayer.Play(model, "ship", "takeoff");
                    spray = MakeSpray(basePos);
                    Services.Audio.PlayStinger("engines");
                }
                float u = t - LiftStart;
                float alt = u <= 0 ? 0 : (u < ClimbTime ? CruiseAlt * Ease.InOut(u / ClimbTime) : CruiseAlt);
                float hover = Ease.InOut(Ease.Span(t, 3f, 8f)) * 2.5f; // code hover when there is no takeoff take
                float fwd = u > 2f ? 1.4f * (u - 2f) * (u - 2f) : 0f;
                if (u > 12f) fwd = 1.4f * 100f + 28f * (u - 12f);
                Vector3 shipPos = basePos + Vector3.up * (alt + hover) + travel * fwd;
                // Turn the prow toward the sun as it climbs, with a slight nose-up.
                float turn = Ease.InOut(Ease.Span(t, 8f, 16f));
                Quaternion toSun = Quaternion.LookRotation(-travel) ; // pivot forward = -nose
                Quaternion shipRot = Quaternion.Slerp(baseRot, toSun, turn) * Quaternion.Euler(-8f * Ease.Span(t, 8f, 12f) * (1f - Ease.Span(t, 16f, 20f)), 0, 0);
                _pivot.SetPositionAndRotation(shipPos, shipRot);
                if (thrust) thrust.intensity = tookOff ? 6f * (0.8f + 0.2f * Mathf.PerlinNoise(t * 9f, 0)) * (1f - Ease.Span(t, 18f, 22f) * 0.6f) : 0f;
                if (spray) { var em = spray.emission; em.enabled = alt < 6f && tookOff; }

                // ---- camera
                Vector3 eye; Vector3 look;
                float shake = tookOff ? Mathf.Max(0, 1f - Ease.Span(t, 3f, 10f)) * 0.06f : 0f;
                if (t < 7f)
                {
                    // A: wide on the ramp, slow push in.
                    eye = Vector3.Lerp(shotA, shotA - door * 2f + Vector3.up * 0.3f, Ease.InOut(t / 7f));
                    look = _pivot.position + shipCenter;
                }
                else if (t < 11.5f)
                {
                    // B: low on the shore, looking up as it climbs into the rain.
                    eye = shotB;
                    look = _pivot.position + shipCenter;
                }
                else if (t < 18f)
                {
                    // C: above and behind, looking down past it at the shrinking sea.
                    float k = Ease.InOut(Ease.Span(t, 11.5f, 13f));
                    Vector3 off = Vector3.Lerp(-travel * 10f + Vector3.up * 2f, -travel * 17f + Vector3.up * 10f, k);
                    eye = _pivot.position + off;
                    look = _pivot.position + travel * 8f - Vector3.up * Mathf.Lerp(4f, 18f, k);
                }
                else
                {
                    // D: alongside over the cloud sea, the ship small against the sun.
                    float k = Ease.InOut(Ease.Span(t, 18f, Length));
                    Vector3 sideV = Vector3.Cross(Vector3.up, travel);
                    eye = _pivot.position + sideV * Mathf.Lerp(16f, 26f, k) - travel * Mathf.Lerp(14f, 6f, k) + Vector3.up * Mathf.Lerp(3f, 6f, k);
                    look = _pivot.position + travel * Mathf.Lerp(30f, 70f, k) + Vector3.up * 1f;
                }
                eye += UnityEngine.Random.insideUnitSphere * shake;
                cam.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));

                // ---- fades: a soft white at the cut into the clouds, then gold at the end
                float cloudFlash = 1f - Mathf.Clamp01(Mathf.Abs(t - 18f) / 0.6f);
                float end = Ease.In(Ease.Span(t, Length - 2.5f, Length));
                fade.Set(Gold, Mathf.Max(cloudFlash * 0.85f, end));

                if (t >= Length && !done)
                {
                    done = true;
                    _onDone?.Invoke();
                }
                if (done)
                {
                    // Linger: the gold lifts off a calm sky under the end screen.
                    fade.Set(Gold, Mathf.Lerp(1f, 0.15f, Ease.Smooth(Ease.Span(t, Length, Length + 3f))));
                }
                yield return null;
            }
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (!root) return null;
            foreach (var tr in root.GetComponentsInChildren<Transform>(true)) if (tr.name == name) return tr;
            return null;
        }

        static Light MakeThrustLight(Transform pivot)
        {
            var go = new GameObject("Thrust");
            go.transform.SetParent(pivot, false);
            go.transform.localPosition = new Vector3(0, -0.2f, 1.5f);
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.color = new Color(1f, 0.6f, 0.3f); l.range = 16f; l.intensity = 0f;
            l.shadows = LightShadows.None;
            return l;
        }

        static ParticleSystem MakeSpray(Vector3 at)
        {
            var go = new GameObject("BloodSpray");
            go.transform.position = at + Vector3.up * 0.2f;
            go.transform.rotation = Quaternion.Euler(-90, 0, 0);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = 1.1f; main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
            main.startColor = new Color(0.55f, 0.02f, 0.02f, 0.9f);
            main.gravityModifier = 1.2f; main.maxParticles = 1500; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 600f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 2.5f; sh.radiusThickness = 0.2f;
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.x = vel.y = vel.z = new ParticleSystem.MinMaxCurve(0f, 0f); vel.radial = new ParticleSystem.MinMaxCurve(6f, 10f);
            var r = go.GetComponent<ParticleSystemRenderer>();
            var shader = Shader.Find("Fnmw/Rain");
            if (shader) { var m = new Material(shader); m.SetColor("_Tint", Color.white); r.sharedMaterial = m; }
            ps.Play();
            return ps;
        }
    }
}
