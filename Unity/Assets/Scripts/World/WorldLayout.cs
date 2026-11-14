using UnityEngine;

namespace Fnmw.World
{
    /// <summary>
    /// Where everything sits on the shore. The level is a strip along X: the art's 20 m shore tiles repeat
    /// in X, dry ground at +Z rising to cliffs, the blood sea at -Z. The player walks +X from the crash
    /// site past the consoles to the ship.
    /// </summary>
    public static class WorldLayout
    {
        public const float MinX = -82f, MaxX = 82f;      // walkable extent along the shore
        public const float WallZ = 12.5f, SeaWallZ = -8.5f;
        public const float TileSize = 20f;
        public const float BaseSeaLevel = 0.36f;           // the art's waterline (z = -4 at rest)
        public const float CloudDeckY = 140f;

        public static readonly Vector3 PlayerSpawn = new Vector3(-66f, 0f, 4.5f);
        public static readonly float PlayerSpawnYaw = 90f;   // facing +X, down the shore

        /// <summary>Console stations in path order. The director picks as many as there are puzzles.</summary>
        public static readonly Vector3[] Consoles =
        {
            new Vector3(-36f, 0f, 2.2f),
            new Vector3(-2f, 0f, -1.2f),    // low, near the waterline: floods as the tide comes in
            new Vector3(32f, 0f, 3.4f),
        };

        public static readonly Vector3 Ship = new Vector3(66f, 0f, 6.0f);
        public static readonly float ShipYaw = 180f;         // door (model +X) faces back down the shore (-X)

        /// <summary>Ground height of the shore profile used by the art (terrain_shore.py), plus cliffs and seabed.</summary>
        public static float Height(float x, float z)
        {
            float yb = -z; // the art was authored with Blender +Y = Unity -Z
            float h;
            if (z > 10f)
            {
                float t = z - 10f;
                h = 1.76f + t * 0.18f + Mathf.Max(0f, t - 3f) * 0.85f
                    + (Mathf.PerlinNoise(x * 0.11f + 3.1f, z * 0.13f) - 0.4f) * Mathf.Clamp01((t - 2f) / 4f) * 6f;
            }
            else if (z < -10f)
            {
                h = -(-10f - z) * 0.06f;
            }
            else
            {
                h = (yb <= 4f ? 0.10f * (4f - yb) : -0.06f * (yb - 4f)) + 0.36f;
                if (yb < 3f)
                {
                    float i = Mathf.Repeat(x + 10f, TileSize) / TileSize * 12f;
                    float j = (yb + 10f) / TileSize * 12f;
                    h += 0.11f * Mathf.Sin(i * Mathf.PI * 2f / 12f) * Mathf.Sin(j * Mathf.PI / 12f);
                }
            }
            return h;
        }

        public static Vector3 OnGround(Vector3 p) => new Vector3(p.x, Height(p.x, p.z), p.z);

        /// <summary>Blood level over the run: a slow visible clock, faster once the final chase starts.</summary>
        public static float SeaLevelAt(float elapsed, float chaseSeconds)
        {
            return BaseSeaLevel + Mathf.Min(elapsed * 0.0032f, 1.05f) + Mathf.Min(chaseSeconds * 0.01f, 0.35f);
        }
    }
}
