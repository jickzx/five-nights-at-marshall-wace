using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Fnmw.Lang
{
    /// <summary>
    /// Seeded alien surface for one source language in one world: a random phonology, a form for every
    /// morph, glyph seeds and the tongue's name. The look is random per seed so the real language stays
    /// hidden until the reveal; only the grammar (the order and agreement patterns) carries over.
    /// </summary>
    public sealed class AlienLexicon
    {
        static readonly string[] ConsonantPool = { "p", "t", "k", "b", "d", "g", "m", "n", "s", "z", "r", "l", "v", "f", "h", "sh", "kh", "th", "zh", "j", "w", "q", "x", "y" };
        static readonly string[] VowelPool = { "a", "e", "i", "o", "u" };
        static readonly string[] Front = { "e", "i" };
        static readonly string[] Back = { "a", "u" };
        static readonly string[][] PunctPairs =
        {
            new[] { "<:", ":>" }, new[] { "{~", "~}" }, new[] { "/^", "^\\" }, new[] { "[%", "%]" }, new[] { "(*", "*)" }, new[] { "|=", "=|" },
        };
        static readonly string[] NameEndings = { "ak", "ith", "uun", "esh", "or", "ae", "ix", "uth" };

        public readonly SourceLanguage Language;
        public readonly string AlienName;
        readonly int _seed;
        readonly Rng _rng;
        readonly string[] _cons;
        readonly string[] _vowels;
        readonly float _finalC, _longV;
        readonly Dictionary<string, string> _forms = new Dictionary<string, string>();
        readonly Dictionary<string, string[]> _roots = new Dictionary<string, string[]>();
        readonly Dictionary<string, string> _groupTemplate = new Dictionary<string, string>();
        readonly Dictionary<string, string[]> _punct = new Dictionary<string, string[]>();
        readonly HashSet<string> _used = new HashSet<string>();
        string _patA, _patI, _patM;

        public AlienLexicon(SourceLanguage lang, int worldSeed, int slot)
        {
            Language = lang;
            _seed = worldSeed;
            _rng = new Rng(Hash.Mix(worldSeed, slot, Hash.Fnv(lang.Code)));
            _cons = _rng.Sample(ConsonantPool, _rng.Range(8, 12)).ToArray();
            _vowels = lang.NeedsHarmony ? VowelPool : _rng.Sample(VowelPool, _rng.Range(3, 6)).ToArray();
            _finalC = 0.15f + _rng.Float() * 0.4f;
            _longV = _rng.Float() * 0.25f;
            AlienName = MakeName();
            foreach (var m in lang.Inventory) Realise(m);
        }

        string C() => _rng.Pick(_cons);
        string V(int harmony = 0)
        {
            string v = harmony == 1 ? _rng.Pick(Front) : harmony == 2 ? _rng.Pick(Back) : _rng.Pick(_vowels);
            return _rng.Chance(_longV) ? v + v : v;
        }

        string Syllable(int harmony, bool allowFinal) => C() + V(harmony) + (allowFinal && _rng.Chance(_finalC) ? C() : "");

        string MakeName()
        {
            var sb = new StringBuilder(Syllable(0, false));
            if (_rng.Chance(0.5f)) sb.Append(Syllable(0, false));
            sb.Append(_rng.Pick(NameEndings));
            var s = sb.ToString();
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        string Unique(System.Func<string> make)
        {
            for (int i = 0; i < 60; i++)
            {
                var s = make();
                if (_used.Add(s)) return s;
            }
            string f = make() + Syllable(0, false);
            while (!_used.Add(f)) f += _rng.Pick(_vowels);
            return f;
        }

        void Realise(MorphSpec m)
        {
            switch (m.Kind)
            {
                case MorphKind.Root:
                    _forms[m.Key] = Unique(() => _rng.Chance(0.35f)
                        ? C() + V(m.Harmony) + C()
                        : Syllable(m.Harmony, false) + Syllable(m.Harmony, true));
                    break;
                case MorphKind.Glyph:
                    _forms[m.Key] = Unique(() => Syllable(0, true));
                    break;
                case MorphKind.Particle:
                    _forms[m.Key] = Unique(() => _rng.Chance(0.3f) ? V() : C() + V());
                    break;
                case MorphKind.Affix:
                    if (m.Group != null && m.Harmony != 0)
                    {
                        // harmonising pair: same consonant skeleton, vowel follows the root's class
                        if (!_groupTemplate.TryGetValue(m.Group, out var tpl))
                        {
                            for (int tries = 0; ; tries++)
                            {
                                tpl = _rng.Chance(0.5f) ? C() + "#" + C() : C() + "#";
                                int k = _rng.Range(0, 2);
                                string f = tpl.Replace("#", Front[k]), b = tpl.Replace("#", Back[k]);
                                if ((!_used.Contains(f) && !_used.Contains(b)) || tries > 50) { tpl += "|" + k; break; }
                            }
                            _groupTemplate[m.Group] = tpl;
                        }
                        var parts = tpl.Split('|');
                        int kk = int.Parse(parts[1]);
                        var form = parts[0].Replace("#", m.Harmony == 1 ? Front[kk] : Back[kk]);
                        _used.Add(form);
                        _forms[m.Key] = form;
                    }
                    else _forms[m.Key] = Unique(() => _rng.Chance(0.5f) ? V() + C() : C() + V() + (_rng.Chance(0.3f) ? C() : ""));
                    break;
                case MorphKind.Punct:
                    if (!_punct.TryGetValue(m.Group, out var pair))
                    {
                        var free = PunctPairs.Where(p => !_punct.Values.Contains(p)).ToList();
                        pair = _rng.Pick(free);
                        _punct[m.Group] = pair;
                    }
                    _forms[m.Key] = m.Closing ? pair[1] : pair[0];
                    break;
                case MorphKind.Root3:
                    for (int tries = 0; ; tries++)
                    {
                        var single = _cons.Where(c => c.Length == 1 && c != "y" && c != "w").ToList();
                        if (single.Count < 4) single = single.Concat(new[] { "k", "t", "b", "r", "s", "m", "n", "d" }).Distinct().ToList();
                        var r = _rng.Sample(single, 3).ToArray();
                        string key = string.Join("", r);
                        if (_used.Add("√" + key) || tries > 50) { _roots[m.Key] = r; _forms[m.Key] = string.Join("-", r); break; }
                    }
                    break;
                case MorphKind.Pattern:
                    if (_patA == null)
                    {
                        var vs = _rng.Sample(_vowels, 2);
                        _patA = vs[0]; _patI = vs[1];
                        _patM = _rng.Pick(_cons.Where(c => c.Length == 1).DefaultIfEmpty("m").ToList());
                    }
                    _forms[m.Key] = m.Shape;
                    break;
            }
        }

        public string Form(string key) => _forms.TryGetValue(key, out var f) ? f : throw new KeyNotFoundException($"{Language.Name}: no alien form for '{key}'");

        /// <summary>Alien text of a word. A lone suffix used as a tile is shown with a leading hyphen.</summary>
        public string Render(Word w, bool asTile = false)
        {
            string s;
            var pattern = w.M.FirstOrDefault(k => Language.Spec(k).Kind == MorphKind.Pattern);
            if (pattern != null)
            {
                var root = w.M.First(k => Language.Spec(k).Kind == MorphKind.Root3);
                var c = _roots[root];
                var sb = new StringBuilder();
                foreach (char ch in Language.Spec(pattern).Shape)
                {
                    switch (ch)
                    {
                        case '1': sb.Append(c[0]); break;
                        case '2': sb.Append(c[1]); break;
                        case '3': sb.Append(c[2]); break;
                        case 'A': sb.Append(_patA); break;
                        case 'I': sb.Append(_patI); break;
                        case 'M': sb.Append(_patM); break;
                        default: sb.Append(ch); break;
                    }
                }
                s = sb.ToString();
            }
            else
            {
                s = string.Concat(w.M.Select(Form));
                if (asTile && w.M.Length == 1 && Language.Spec(w.M[0]).Kind == MorphKind.Affix) s = "-" + s;
            }
            return Language.UpperCase ? s.ToUpperInvariant() : s;
        }

        public string Render(IEnumerable<Word> phrase) => string.Join(" ", phrase.Select(w => Render(w)));

        public int GlyphSeed(Word w) => (int)(Hash.Fnv(Language.Code + ":" + w.Id) ^ (uint)_seed);
    }
}
