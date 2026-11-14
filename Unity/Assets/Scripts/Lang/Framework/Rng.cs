using System.Collections.Generic;

namespace Fnmw.Lang
{
    /// <summary>
    /// SplitMix64. Deterministic on every runtime (System.Random and string.GetHashCode are not
    /// guaranteed to be), so a seed always produces the same world.
    /// </summary>
    public sealed class Rng
    {
        ulong _s;

        public Rng(ulong seed) { _s = seed; }

        public ulong Next()
        {
            _s += 0x9E3779B97F4A7C15UL;
            ulong z = _s;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>Integer in [min, max).</summary>
        public int Range(int min, int max) => max <= min ? min : min + (int)(Next() % (ulong)(max - min));

        public float Float() => (Next() >> 40) / (float)(1UL << 24);

        public bool Chance(float p) => Float() < p;

        public T Pick<T>(IList<T> list) => list[Range(0, list.Count)];

        public void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        /// <summary>k distinct items, in random order.</summary>
        public List<T> Sample<T>(IList<T> list, int k)
        {
            var copy = new List<T>(list);
            Shuffle(copy);
            if (k < copy.Count) copy.RemoveRange(k, copy.Count - k);
            return copy;
        }
    }

    public static class Hash
    {
        /// <summary>FNV-1a, stable across runtimes.</summary>
        public static uint Fnv(string s)
        {
            uint h = 2166136261;
            if (s == null) return h;
            foreach (char c in s) { h ^= c; h *= 16777619; }
            return h;
        }

        public static ulong Mix(params long[] parts)
        {
            ulong h = 0xCBF29CE484222325UL;
            foreach (var p in parts)
            {
                h ^= (ulong)p;
                h *= 0x100000001B3UL;
                h ^= h >> 29;
            }
            return h;
        }
    }
}
