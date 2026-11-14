using System;
using System.Collections;
using Fnmw.Core;
using UnityEngine;

namespace Fnmw.Game
{
    /// <summary>
    /// The martyr ending. Not a splatter: the beast takes you, the view whips to it and sinks to its knees,
    /// tilts slowly up into the falling blood, then cuts to a low shot of a kneeling silhouette with its arms
    /// open against a glowing red sky, and the red burns out to white. About fourteen seconds.
    /// The World's Atmosphere yields fog and sky to this script during the Dead phase.
    /// </summary>
    public class DeathCutscene : MonoBehaviour
    {
        public const float Length = 14f;
        static readonly Color DeepRed = new Color(0.42f, 0.02f, 0.02f);
        static readonly Color GlowRed = new Color(0.95f, 0.16f, 0.08f);
        static readonly Color White = new Color(1f, 0.97f, 0.94f);

        Action _onDone;

        public void Play(Action onDone)
        {
            _onDone = onDone;
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            var cam = Camera.main ? Camera.main.transform : null;
            var playerT = Services.Player;
            var player = playerT ? playerT.GetComponent<PlayerController>() : null;
            if (!cam || !playerT) { yield return new WaitForSeconds(1f); _onDone?.Invoke(); yield break; }
            var fade = ScreenFade.Create(transform);
            var beast = GameDirector.FindBeast();

            Vector3 feet = playerT.position;
            Vector3 eye0 = cam.position;
            Quaternion rot0 = cam.rotation;
            Vector3 toBeast = beast ? beast.position - feet : playerT.forward * 3f;
            toBeast.y = 0;
            if (toBeast.sqrMagnitude < 0.01f) toBeast = playerT.forward;
            Vector3 fwd = toBeast.normalized;
            float beastH = beast ? BeastHeight(beast) : 2.5f;
            Vector3 beastFace = beast ? beast.position + Vector3.up * beastH * 0.8f : feet + fwd * 3f + Vector3.up * 2.4f;
            playerT.rotation = Quaternion.LookRotation(fwd);
            cam.SetParent(null, true);

            Color fog0 = RenderSettings.fogColor;
            float dens0 = RenderSettings.fogDensity;
            Color amb0 = RenderSettings.ambientLight;

            float t = 0f;
            while (t < 6f)
            {
                t += Time.deltaTime;
                // 0-0.7: whip to the beast with a red flash. 0.7-2.8: sink to the knees. 2.8-6: tilt up to the sky.
                float whip = Ease.Out(t / 0.7f);
                float sink = Ease.InOut(Ease.Span(t, 0.7f, 2.8f));
                float tilt = Ease.InOut(Ease.Span(t, 2.8f, 6f));
                float shake = Mathf.Max(0, 1f - t / 1.4f);
                Vector3 eye = Vector3.Lerp(eye0, feet + Vector3.up * 0.82f + fwd * 0.15f, sink);
                eye += UnityEngine.Random.insideUnitSphere * 0.05f * shake;
                Quaternion faceBeast = Quaternion.LookRotation(beastFace - eye);
                Quaternion up = Quaternion.LookRotation(fwd * 0.28f + Vector3.up) * Quaternion.Euler(0, 0, 4f);
                Quaternion r = Quaternion.Slerp(rot0, faceBeast, whip);
                r = Quaternion.Slerp(r, faceBeast * Quaternion.Euler(6f, 0, 7f), sink * (1f - tilt));
                r = Quaternion.Slerp(r, up, tilt);
                cam.SetPositionAndRotation(eye, r * Quaternion.Euler(UnityEngine.Random.Range(-2f, 2f) * shake, UnityEngine.Random.Range(-2f, 2f) * shake, 0));
                fade.Set(new Color(0.6f, 0f, 0f), 0.55f * Mathf.Max(0, 1f - t / 1.2f));
                // The storm sky starts to glow from within.
                RenderSettings.fogColor = Color.Lerp(fog0, DeepRed, Ease.Smooth(Ease.Span(t, 1.5f, 6f)));
                RenderSettings.fogDensity = Mathf.Lerp(dens0, 0.04f, Ease.Span(t, 1.5f, 6f));
                RenderSettings.ambientLight = Color.Lerp(amb0, new Color(0.25f, 0.05f, 0.05f), Ease.Span(t, 1.5f, 6f));
                Shader.SetGlobalColor("_FnmwAmbient", RenderSettings.ambientLight);
                yield return null;
            }

            // Cut: the silhouette. The figure kneels where the player stood, facing the beast.
            if (player) player.SetFlashlight(false);
            var silhouette = MakeSilhouetteMaterial();
            GameObject figure = SpawnFigure(player, playerT, fwd, silhouette);
            if (beast) foreach (var rr in beast.GetComponentsInChildren<Renderer>()) if (!(rr is ParticleSystemRenderer)) rr.sharedMaterial = silhouette;
            var side = Vector3.Cross(Vector3.up, fwd);
            // Orbit toward the sea (-Z), away from the cliffs and their boulders.
            float orbitSign = Vector3.Dot(Quaternion.AngleAxis(30f, Vector3.up) * -fwd, Vector3.back) > Vector3.Dot(Quaternion.AngleAxis(-30f, Vector3.up) * -fwd, Vector3.back) ? 1f : -1f;
            Vector3 beastFrom = beast ? beast.position : Vector3.zero;
            Vector3 beastTo = feet + fwd * 8.5f; beastTo.y = beastFrom.y;

            while (t < Length)
            {
                t += Time.deltaTime;
                float k = Ease.Span(t, 6f, Length);
                // Low behind-and-beside, slowly orbiting out to a profile and craning up.
                float ang = Mathf.Lerp(12f, 34f, Ease.InOut(k)) * orbitSign;
                // The beast lets go and withdraws into the red, a shape in the mist.
                if (beast) beast.position = Vector3.Lerp(beastFrom, beastTo, Ease.InOut(Ease.Span(t, 6f, 10f)));
                Vector3 back = Quaternion.AngleAxis(ang, Vector3.up) * -fwd;
                float dist = Mathf.Lerp(3.6f, 5.4f, Ease.InOut(k));
                Vector3 eye = feet + back * dist + Vector3.up * Mathf.Lerp(0.3f, 0.8f, Ease.InOut(k));
                Vector3 look = feet + Vector3.up * Mathf.Lerp(1.5f, 2.1f, k) + fwd * 1.5f;
                cam.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));

                float red = Ease.Smooth(Ease.Span(t, 6f, 8.5f));
                float white = Ease.InOut(Ease.Span(t, 8.5f, 12.5f));
                Color fog = Color.Lerp(Color.Lerp(DeepRed, GlowRed, red), White, white);
                RenderSettings.fogColor = fog;
                RenderSettings.fogDensity = Mathf.Lerp(Mathf.Lerp(0.04f, 0.055f, red), 0.075f, white);
                RenderSettings.ambientLight = Color.Lerp(new Color(0.25f, 0.05f, 0.05f), new Color(0.9f, 0.88f, 0.86f), white);
                Shader.SetGlobalColor("_FnmwAmbient", RenderSettings.ambientLight);
                fade.Set(White, Ease.In(Ease.Span(t, 11.2f, 13.6f)) * 0.92f);
                yield return null;
            }

            _onDone?.Invoke();
            // Leave the white tableau behind the end screen.
            float f = fade.Alpha;
            for (float u = 0; u < 2.5f; u += Time.deltaTime)
            {
                fade.Set(White, Mathf.Lerp(f, 0.25f, Ease.Smooth(u / 2.5f)));
                yield return null;
            }
            if (figure) { } // stays kneeling
        }

        static float BeastHeight(Transform beast)
        {
            var rs = beast.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return 2.5f;
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            return Mathf.Clamp(b.max.y - beast.position.y, 1.2f, 6f);
        }

        static Material MakeSilhouetteMaterial()
        {
            var sh = Shader.Find("Fnmw/PS1Lit");
            var m = sh ? new Material(sh) : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.name = "silhouette";
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.black);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
            return m;
        }

        GameObject SpawnFigure(PlayerController player, Transform playerT, Vector3 fwd, Material mat)
        {
            var body = player ? player.Body : null;
            if (body && ModelLoader.HasArt("player"))
            {
                // The art faces -Z, the controller +Z.
                body.transform.localRotation = Quaternion.Euler(0, 180f, 0);
                foreach (var r in body.GetComponentsInChildren<Renderer>())
                {
                    r.enabled = true;
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                    r.sharedMaterials = mats;
                }
                var cp = ClipPlayer.Play(body, "player", "death_martyr", 1f, 2.0f);
                if (cp) return body;
                foreach (var r in body.GetComponentsInChildren<Renderer>()) r.enabled = false;
            }
            var fig = MartyrFigure.Build(playerT, mat);
            fig.transform.localPosition = Vector3.zero;
            fig.transform.localRotation = Quaternion.identity;
            return fig.gameObject;
        }
    }
}
