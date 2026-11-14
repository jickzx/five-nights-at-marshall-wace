using UnityEngine;

namespace Fnmw.Game
{
    /// <summary>
    /// A code-built silhouette of the survivor for the death ending when the player art (or its
    /// death_martyr take) is missing: kneels, lifts its head and opens its arms to the sky.
    /// </summary>
    public class MartyrFigure : MonoBehaviour
    {
        Transform _hips, _torso, _head, _armL, _armR, _foreL, _foreR, _thighL, _thighR, _shinL, _shinR;
        float _t;

        public static MartyrFigure Build(Transform parent, Material mat)
        {
            var root = new GameObject("MartyrFigure");
            root.transform.SetParent(parent, false);
            var f = root.AddComponent<MartyrFigure>();
            Transform Part(string n, Transform p, Vector3 pos, PrimitiveType type, Vector3 scale, Vector3 meshOffset)
            {
                var j = new GameObject(n).transform;
                j.SetParent(p, false); j.localPosition = pos;
                var m = GameObject.CreatePrimitive(type);
                Destroy(m.GetComponent<Collider>());
                m.transform.SetParent(j, false);
                m.transform.localScale = scale; m.transform.localPosition = meshOffset;
                m.GetComponent<MeshRenderer>().sharedMaterial = mat;
                return j;
            }
            f._hips = Part("hips", root.transform, new Vector3(0, 0.95f, 0), PrimitiveType.Cube, new Vector3(0.36f, 0.18f, 0.22f), Vector3.zero);
            f._torso = Part("torso", f._hips, new Vector3(0, 0.08f, 0), PrimitiveType.Cube, new Vector3(0.44f, 0.58f, 0.26f), new Vector3(0, 0.3f, 0));
            f._head = Part("head", f._torso, new Vector3(0, 0.62f, 0), PrimitiveType.Sphere, new Vector3(0.27f, 0.3f, 0.28f), new Vector3(0, 0.14f, 0));
            f._armL = Part("armL", f._torso, new Vector3(-0.26f, 0.52f, 0), PrimitiveType.Cube, new Vector3(0.11f, 0.32f, 0.11f), new Vector3(0, -0.16f, 0));
            f._armR = Part("armR", f._torso, new Vector3(0.26f, 0.52f, 0), PrimitiveType.Cube, new Vector3(0.11f, 0.32f, 0.11f), new Vector3(0, -0.16f, 0));
            f._foreL = Part("foreL", f._armL, new Vector3(0, -0.32f, 0), PrimitiveType.Cube, new Vector3(0.09f, 0.3f, 0.09f), new Vector3(0, -0.15f, 0));
            f._foreR = Part("foreR", f._armR, new Vector3(0, -0.32f, 0), PrimitiveType.Cube, new Vector3(0.09f, 0.3f, 0.09f), new Vector3(0, -0.15f, 0));
            f._thighL = Part("thighL", f._hips, new Vector3(-0.11f, -0.05f, 0), PrimitiveType.Cube, new Vector3(0.14f, 0.45f, 0.15f), new Vector3(0, -0.22f, 0));
            f._thighR = Part("thighR", f._hips, new Vector3(0.11f, -0.05f, 0), PrimitiveType.Cube, new Vector3(0.14f, 0.45f, 0.15f), new Vector3(0, -0.22f, 0));
            f._shinL = Part("shinL", f._thighL, new Vector3(0, -0.45f, 0), PrimitiveType.Cube, new Vector3(0.12f, 0.45f, 0.13f), new Vector3(0, -0.22f, 0));
            f._shinR = Part("shinR", f._thighR, new Vector3(0, -0.45f, 0), PrimitiveType.Cube, new Vector3(0.12f, 0.45f, 0.13f), new Vector3(0, -0.22f, 0));
            f.Pose(0);
            return f;
        }

        void Update() { _t += Time.deltaTime; Pose(_t); }

        /// <summary>0-1.5 s sink to the knees, 1.5-4 s head lifts and the arms open wide.</summary>
        void Pose(float t)
        {
            float kneel = Ease.Smooth(t / 1.5f);
            float open = Ease.InOut((t - 1.4f) / 2.6f);
            _hips.localPosition = new Vector3(0, Mathf.Lerp(0.95f, 0.55f, kneel), 0);
            _thighL.localRotation = _thighR.localRotation = Quaternion.Euler(Mathf.Lerp(0, -12f, kneel), 0, 0);
            _shinL.localRotation = _shinR.localRotation = Quaternion.Euler(Mathf.Lerp(0, 95f, kneel), 0, 0);
            _torso.localRotation = Quaternion.Euler(Mathf.Lerp(Mathf.Lerp(0, 18f, kneel), -14f, open), 0, 0);
            _head.localRotation = Quaternion.Euler(Mathf.Lerp(Mathf.Lerp(0, 25f, kneel), -38f, open), 0, 0);
            float spread = Mathf.Lerp(8f, 105f, open);
            float lift = Mathf.Lerp(0, -20f, open);
            _armL.localRotation = Quaternion.Euler(lift, 0, -spread);
            _armR.localRotation = Quaternion.Euler(lift, 0, spread);
            _foreL.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(-10f, -12f, open));
            _foreR.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(10f, 12f, open));
        }
    }
}
