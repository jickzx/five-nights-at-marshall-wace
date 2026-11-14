using Fnmw.Core;
using UnityEngine;

namespace Fnmw.World
{
    /// <summary>The red sea. A faceted grid whose level rises over the run; it writes GameState.SeaLevel.</summary>
    public class BloodSea : MonoBehaviour
    {
        float _chaseTime;
        public float Level { get; private set; } = WorldLayout.BaseSeaLevel;

        public static BloodSea Create(Transform parent)
        {
            var go = new GameObject("BloodSea");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = Grid(560f, 560f, 280, 280);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = new Material(Ps1Look.Load("BloodSea")) { name = "blood_sea" };
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            // A coarse far sea under the detailed one, out to the horizon, so it reads as an ocean from the air.
            var far = new GameObject("FarSea");
            far.transform.SetParent(go.transform, false);
            far.transform.localPosition = new Vector3(0, -0.7f, 0);
            far.AddComponent<MeshFilter>().sharedMesh = Grid(5000f, 5000f, 50, 50);
            var fr = far.AddComponent<MeshRenderer>();
            fr.sharedMaterial = mr.sharedMaterial;
            fr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var sea = go.AddComponent<BloodSea>();
            go.transform.position = new Vector3(0, WorldLayout.BaseSeaLevel, -60f);
            return sea;
        }

        static Mesh Grid(float w, float d, int nx, int nz)
        {
            var v = new Vector3[(nx + 1) * (nz + 1)];
            for (int j = 0; j <= nz; j++)
            for (int i = 0; i <= nx; i++)
                v[j * (nx + 1) + i] = new Vector3((i / (float)nx - 0.5f) * w, 0, (j / (float)nz - 0.5f) * d);
            var t = new int[nx * nz * 6];
            int k = 0;
            for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, e = c + 1;
                t[k++] = a; t[k++] = c; t[k++] = e; t[k++] = a; t[k++] = e; t[k++] = b;
            }
            var m = new Mesh { name = "blood_sea_grid", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.vertices = v; m.triangles = t;
            m.RecalculateNormals();
            m.bounds = new Bounds(Vector3.zero, new Vector3(w, 4f, d));
            return m;
        }

        void Update()
        {
            var s = Services.State;
            if (s.Phase == GamePhase.Chase) _chaseTime += Time.deltaTime;
            float target = WorldLayout.SeaLevelAt(s.Elapsed, _chaseTime);
            Level = Mathf.MoveTowards(Level, target, Time.deltaTime * 0.2f);
            var p = transform.position;
            transform.position = new Vector3(p.x, Level, p.z);
            s.SeaLevel = Level;
        }
    }
}
