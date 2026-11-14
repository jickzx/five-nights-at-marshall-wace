using System;
using System.Collections.Generic;
using System.Linq;

namespace Fnmw.Lang
{
    public enum MorphKind
    {
        Root,      // content word or stem
        Affix,     // bound suffix, shown as "-xx" when it is a tile on its own
        Particle,  // short free-standing function word
        Punct,     // sentence mark
        Glyph,     // the reading of one meaningful kanji inside a word
        Root3,     // Arabic three-consonant root, only realised inside a Pattern
        Pattern,   // Arabic vowel pattern the root is set into
    }

    /// <summary>One morpheme of a real language, with its real (romanised) written form.</summary>
    public sealed class MorphSpec
    {
        public string Key;        // unique in its language, e.g. "door", "PL.lar"
        public MorphKind Kind;
        public string Form;       // the real form, romanised: "akete", "lar", "zhī", "¿"
        public string Gloss;      // English gloss
        public string Concept;    // concept a tile carrying this morph exercises (null = vocabulary)

        public MorphSpec(string key, MorphKind kind, string form, string gloss, string concept = null)
        {
            Key = key; Kind = kind; Form = form; Gloss = gloss; Concept = concept;
        }
    }

    /// <summary>A word: an ordered list of morph keys. Its language renders it to real text.</summary>
    public sealed class Word
    {
        public readonly string[] M;
        public Word(params string[] m) { M = m; }
        public string Id => string.Join("+", M);
        public override string ToString() => Id;
    }

    /// <summary>A rendering of a meaning under hypothesis h (0 = the true grammar).</summary>
    public delegate List<Word> Render(int h);

    public sealed class DraftExample
    {
        public string Picture;
        public Render Render;
        public bool Optional;   // may be dropped at high difficulty if the puzzle stays uniquely solvable
    }

    /// <summary>
    /// A puzzle before it gets text and tiles. Built by a language's PuzzleKind.
    /// Hypotheses: index 0 is the true grammar; every other index is a rival grammar a player might
    /// believe in (English word order, no agreement...). HypConcepts[h] names what a player using rival h misunderstands.
    /// </summary>
    public sealed class Draft
    {
        public string Kind;
        public string Concept;
        public string[] Concepts = Array.Empty<string>();
        public bool MorphTiles;                   // tiles are single morphs (stems, suffixes) instead of whole words
        public string OrderConcept;               // concept for "right tiles, wrong order"
        public readonly List<DraftExample> Examples = new List<DraftExample>();
        public string TargetMeaning;
        public Render Target;
        public string[] HypConcepts = { null };
        public readonly List<(List<Word> phrase, string concept)> Mistakes = new List<(List<Word>, string)>();
        public readonly List<(Word word, string concept)> Extras = new List<(Word, string)>(); // extra distractor words
        public readonly List<List<Word>> AlsoCorrect = new List<List<Word>>(); // other orders the real language accepts, same tiles
        public string RealText;      // the real phrase in its own script plus romanisation, e.g. "扉を開けて (tobira o akete)"
        public string Translation;   // English translation of RealText
        public string Feature;       // one or two plain sentences, true of the real language
        public string NowYouKnow;    // one memorable true fact

        public Draft Example(string picture, Render r, bool optional = false)
        {
            Examples.Add(new DraftExample { Picture = picture, Render = r, Optional = optional });
            return this;
        }

        public Draft Mistake(List<Word> phrase, string concept) { Mistakes.Add((phrase, concept)); return this; }
        public Draft Extra(Word w, string concept = null) { Extras.Add((w, concept)); return this; }
        public Draft Also(List<Word> phrase) { AlsoCorrect.Add(phrase); return this; }
    }

    public static class Hyps
    {
        /// <summary>One rival per bit and every combination. Rival concept = concept of its lowest set bit.</summary>
        public static string[] Bits(params string[] concepts)
        {
            int n = 1 << concepts.Length;
            var r = new string[n];
            for (int h = 1; h < n; h++)
                for (int b = 0; b < concepts.Length; b++)
                    if ((h & (1 << b)) != 0) { r[h] = concepts[b]; break; }
            return r;
        }

        public static bool On(int h, int bit) => (h & (1 << bit)) != 0;
    }

    public sealed class PuzzleKind
    {
        public string Name;
        public int Tier;                 // 0 easiest .. 3 hardest
        public Func<Rng, int, Draft> Build; // (rng, tier) -> draft
        public PuzzleKind(string name, int tier, Func<Rng, int, Draft> build) { Name = name; Tier = tier; Build = build; }
    }

    /// <summary>A real language, its morph inventory, its puzzle kinds and its reveal/quiz content.</summary>
    public abstract class SourceLanguage
    {
        public abstract string Name { get; }
        public abstract string Code { get; }
        public abstract string Endonym { get; }            // the language's own name, romanised: "Nihongo"
        public abstract string Summary { get; }            // one line for the judges' overlay
        public abstract string[] QuizDistractors { get; }  // plausible wrong answers (other real languages)
        public abstract string QuizExplanation { get; }

        readonly List<MorphSpec> _inventory = new List<MorphSpec>();
        readonly Dictionary<string, MorphSpec> _byKey = new Dictionary<string, MorphSpec>();
        readonly List<PuzzleKind> _kinds = new List<PuzzleKind>();

        public IReadOnlyList<MorphSpec> Inventory => _inventory;
        public IReadOnlyList<PuzzleKind> Kinds => _kinds;
        public MorphSpec Spec(string key) => _byKey.TryGetValue(key, out var s) ? s : throw new KeyNotFoundException($"{Name}: no morph '{key}'");
        public bool Has(string key) => _byKey.ContainsKey(key);

        protected MorphSpec Add(string key, MorphKind kind, string form, string gloss, string concept = null)
        {
            var m = new MorphSpec(key, kind, form, gloss, concept);
            _inventory.Add(m);
            _byKey.Add(key, m);
            return m;
        }

        protected void Kind(string name, int tier, Func<Rng, int, Draft> build) => _kinds.Add(new PuzzleKind(name, tier, build));

        /// <summary>The real written form of a word: its morph forms joined. Languages with spelling rules override it.</summary>
        public virtual string Surface(Word w) => string.Concat(w.M.Select(k => Spec(k).Form));

        /// <summary>Text of a word. A lone suffix used as a tile is shown with a leading hyphen.</summary>
        public string Render(Word w, bool asTile = false)
        {
            string s = Surface(w);
            return asTile && w.M.Length == 1 && Spec(w.M[0]).Kind == MorphKind.Affix ? "-" + s : s;
        }

        /// <summary>A phrase as it is written: words spaced, opening marks (¿ ¡) on the next word, closing marks on the last.</summary>
        public string Render(IEnumerable<Word> phrase)
        {
            var sb = new System.Text.StringBuilder();
            bool glue = true;
            foreach (var w in phrase)
            {
                bool punct = w.M.Length == 1 && Spec(w.M[0]).Kind == MorphKind.Punct;
                string s = Render(w);
                bool opening = punct && (s == "¿" || s == "¡");
                if (!glue && !(punct && !opening)) sb.Append(' ');
                sb.Append(s);
                glue = opening;
            }
            return sb.ToString();
        }

        public int GlyphSeed(Word w) => (int)Hash.Fnv(Code + ":" + w.Id);

        /// <summary>The hardest kind not above the tier, else the easiest kind.</summary>
        public PuzzleKind KindFor(int tier, Rng rng)
        {
            int best = _kinds.Where(k => k.Tier <= tier).Select(k => k.Tier).DefaultIfEmpty(-1).Max();
            var pool = best < 0 ? _kinds.Where(k => k.Tier == _kinds.Min(x => x.Tier)).ToList() : _kinds.Where(k => k.Tier == best).ToList();
            return rng.Pick(pool);
        }

        protected static Word W(params string[] m) => new Word(m);
        protected static List<Word> P(params Word[] w) => new List<Word>(w);
        protected static List<Word> Cat(params IEnumerable<Word>[] parts) => parts.SelectMany(p => p).ToList();
    }
}
