using System.Text;

namespace Fnmw.UI
{
    /// <summary>
    /// Unity's legacy text draws characters left to right exactly as given. Arabic needs two things
    /// first: each letter swapped for its joined form (start, middle, end or alone), and the run
    /// reversed so it reads right to left. Hebrew only needs the reversal. This covers the base
    /// alphabets, which is what the language engine emits; it is not a full bidi implementation.
    /// </summary>
    public static class Rtl
    {
        // Joining type of U+0621..U+063A and U+0641..U+064A: U = never joins, R = joins to the
        // previous letter only, D = joins both sides. Presentation Forms-B lists the letters in the
        // same order from U+FE80, with 1, 2 or 4 forms each (alone, end, start, middle).
        const string TypesA = "URRRRDRDRDDDDDRRRRDDDDDDDD";
        const string TypesB = "DDDDDDDRRD";
        static readonly char[] Type = new char[0x64B - 0x621];
        static readonly int[] Form = new int[0x64B - 0x621];

        static Rtl()
        {
            int next = 0xFE80;
            for (int i = 0; i < Type.Length; i++)
            {
                int cp = 0x621 + i;
                char t = cp <= 0x63A ? TypesA[i] : cp >= 0x641 ? TypesB[cp - 0x641] : cp == 0x640 ? 'T' : 'X';
                Type[i] = t;
                Form[i] = next;
                if (t == 'U') next += 1; else if (t == 'R') next += 2; else if (t == 'D') next += 4;
            }
        }

        static char TypeOf(char c) => c >= 0x621 && c <= 0x64A ? Type[c - 0x621] : 'X';
        static bool IsMark(char c) => c >= 0x64B && c <= 0x65F || c == 0x670;
        static bool JoinsNext(char c) { char t = TypeOf(c); return t == 'D' || t == 'T'; }
        static bool JoinsPrev(char c) { char t = TypeOf(c); return t == 'D' || t == 'R' || t == 'T'; }

        public static bool IsRtl(char c) =>
            (c >= 0x590 && c <= 0x6FF) || (c >= 0x750 && c <= 0x77F) || (c >= 0xFB1D && c <= 0xFDFF) || (c >= 0xFE70 && c <= 0xFEFF);

        public static bool HasRtl(string s)
        {
            if (s != null) foreach (char c in s) if (IsRtl(c)) return true;
            return false;
        }

        /// <summary>Arabic letters in logical order to their joined presentation forms (still logical order).</summary>
        public static string Shape(string s)
        {
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                char t = TypeOf(c);
                if (t == 'X' || t == 'T') { sb.Append(c); continue; }
                int p = i - 1; while (p >= 0 && IsMark(s[p])) p--;
                int n = i + 1; while (n < s.Length && IsMark(s[n])) n++;
                bool prev = p >= 0 && JoinsNext(s[p]) && JoinsPrev(c);
                bool nxt = n < s.Length && JoinsNext(c) && JoinsPrev(s[n]);
                // lam + alef fuse into one glyph
                if (c == 0x644 && n < s.Length && (s[n] == 0x627 || s[n] == 0x622 || s[n] == 0x623 || s[n] == 0x625))
                {
                    int lig = s[n] == 0x622 ? 0xFEF5 : s[n] == 0x623 ? 0xFEF7 : s[n] == 0x625 ? 0xFEF9 : 0xFEFB;
                    sb.Append((char)(lig + (prev ? 1 : 0)));
                    i = n;
                    continue;
                }
                int form = t == 'U' ? 0 : t == 'R' ? (prev ? 1 : 0) : prev && nxt ? 3 : nxt ? 2 : prev ? 1 : 0;
                sb.Append((char)(Form[c - 0x621] + form));
            }
            return sb.ToString();
        }

        /// <summary>
        /// The string as it should be handed to a left-to-right renderer: Arabic shaped, and every
        /// right-to-left run (with the spaces and punctuation inside it) reversed in place.
        /// </summary>
        public static string Visual(string s)
        {
            if (!HasRtl(s)) return s;
            s = Shape(s);
            var chars = s.ToCharArray();
            int i = 0;
            while (i < chars.Length)
            {
                if (!IsRtl(chars[i])) { i++; continue; }
                int end = i, j = i;
                while (j < chars.Length && (IsRtl(chars[j]) || chars[j] == ' ' || chars[j] == '،' || chars[j] == '-')) { if (IsRtl(chars[j])) end = j; j++; }
                System.Array.Reverse(chars, i, end - i + 1);
                i = end + 1;
            }
            return new string(chars);
        }
    }
}
