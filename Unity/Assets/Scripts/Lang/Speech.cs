using System.Text.RegularExpressions;

namespace Fnmw.Lang
{
    /// <summary>
    /// The sentence a speech engine should read aloud, taken from RevealInfo.RealText. RealText comes in three
    /// shapes: native first with the reading in brackets (Japanese "扉を開けて (tobira o akete)", Mandarin
    /// "我看这本书。(Wǒ kàn zhè běn shū.)"), the reading first with the script in brackets (Arabic
    /// "kataba l-waladu (كتب الولد)"), or plain Latin text ("Le chat noir dort."). Only the native-script side
    /// is spoken, never both, so the sentence is not read twice.
    /// </summary>
    public static class Speech
    {
        static readonly Regex Bracketed = new Regex(@"^(.*?)\s*[\(（]([^\)）]*)[\)）]\s*$", RegexOptions.Compiled);

        /// <summary>Upper bound on SpeechText. Measured over 120 seeds x 5 consoles x 4 difficulties (SpeechAndHintTests,
        /// Nov 2026): longest de=39, fr=31, tr=28, es=26, ar=20, ja=10, zh=8 characters. 80 leaves room for new sentence
        /// kinds; the test fails if the engine ever exceeds it. The backend's pronounce limit is the same number.</summary>
        public const int MaxLength = 80;

        public static string Clean(string realText)
        {
            string s = (realText ?? "").Trim();
            if (s.Length == 0) return "";
            var m = Bracketed.Match(s);
            if (m.Success && m.Groups[1].Value.Trim().Length > 0)
            {
                string outside = m.Groups[1].Value.Trim(), inside = m.Groups[2].Value.Trim();
                bool outNative = NativeScript(outside), inNative = NativeScript(inside);
                if (outNative && !inNative) return outside;
                if (inNative && !outNative) return inside;
                // neither side is native script: a Latin sentence with a bracketed aside is not ours to guess at
                return outNative ? outside : s;
            }
            return s;
        }

        /// <summary>True when the text has a letter outside the Latin alphabet and its phonetic extras
        /// (transliterations use letters such as ṭ and ḥ from Latin Extended Additional; those are not native script).</summary>
        public static bool NativeScript(string s)
        {
            if (s != null) foreach (char c in s) if (char.IsLetter(c) && !Latin(c)) return true;
            return false;
        }

        static bool Latin(char c) =>
            c < 0x370                              // Basic Latin through IPA and spacing modifiers
            || (c >= 0x1D00 && c <= 0x1DBF)        // phonetic extensions
            || (c >= 0x1E00 && c <= 0x1EFF)        // Latin Extended Additional (ṭ, ḥ, ạ)
            || (c >= 0x2C60 && c <= 0x2C7F) || (c >= 0xA720 && c <= 0xA7FF)
            || (c >= 0xFF21 && c <= 0xFF5A);       // full-width Latin
    }
}
