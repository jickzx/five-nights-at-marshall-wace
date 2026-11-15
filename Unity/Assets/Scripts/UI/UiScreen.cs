using System;
using System.Collections.Generic;
using Fnmw.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Fnmw.UI
{
    /// <summary>A full-stage screen. Rebuilt from scratch every time it opens; ticked by GameUI.</summary>
    internal abstract class UiScreen
    {
        protected readonly GameUI Ui;
        public readonly RectTransform Root;
        protected readonly List<Hit> Hits = new List<Hit>();
        protected float T;

        protected UiScreen(GameUI ui, string name)
        {
            Ui = ui;
            Root = Kit.Node(name, ui.Rig.Stage, 0, 0, UiRig.StageW, UiRig.StageH);
            Root.gameObject.SetActive(false);
        }

        public bool Active => Root && Root.gameObject.activeSelf;
        public abstract GlassMode Glass { get; }

        protected void Open()
        {
            Kit.Clear(Root);
            Hits.Clear();
            T = 0;
            Root.gameObject.SetActive(true);
        }

        public virtual void Close()
        {
            if (Root) Root.gameObject.SetActive(false);
        }

        public void Tick(float dt, UiInput input)
        {
            T += dt;
            if (Pointer(input)) input.Click = false;
            OnTick(dt, input);
        }

        protected abstract void OnTick(float dt, UiInput input);

        protected Hit AddHit(RectTransform rt, Action click, Action<bool> hover = null)
        {
            var h = new Hit { Rt = rt, OnClick = click, OnHover = hover };
            Hits.Add(h);
            return h;
        }

        static readonly Vector3[] Corners = new Vector3[4];

        bool Pointer(UiInput input)
        {
            bool consumed = false;
            for (int i = 0; i < Hits.Count; i++)
            {
                var h = Hits[i];
                bool over = false;
                if (input.HasPointer && h.Rt && h.Rt.gameObject.activeInHierarchy)
                {
                    h.Rt.GetWorldCorners(Corners);
                    Vector2 a = Ui.Rig.Stage.InverseTransformPoint(Corners[0]);
                    Vector2 b = Ui.Rig.Stage.InverseTransformPoint(Corners[2]);
                    over = input.Pointer.x >= a.x && input.Pointer.x <= b.x && input.Pointer.y >= a.y && input.Pointer.y <= b.y;
                }
                if (over != h.Hovered) { h.Hovered = over; h.OnHover?.Invoke(over); }
                if (over && input.Click && !consumed) { consumed = true; h.OnClick?.Invoke(); if (!Active) return true; }
            }
            return consumed;
        }
    }

    /// <summary>Pieces shared by several screens.</summary>
    internal static class Widgets
    {
        public const int GlyphPx = 32;

        /// <summary>A glyph with its sound written underneath. Returns the cell width.</summary>
        public static float Word(Transform parent, float x, float y, Tile tile, ScriptFamily fam, Color glyph, Color caption,
            out RawImage glyphImg, out Text capText)
        {
            string alien = tile?.Alien ?? "";
            float w = WordWidth(tile);
            int seed = tile != null ? tile.GlyphSeed : 0;
            glyphImg = Kit.Pic("Glyph", parent, GlyphGen.Texture(seed, fam, GlyphPx), glyph, x + (w - GlyphPx) / 2f, y, GlyphPx, GlyphPx);
            capText = Kit.Label("Sound", parent, alien, 16, caption, TextAnchor.MiddleCenter, x, y + GlyphPx + 1, w, 16);
            return w;
        }

        /// <summary>The cell width <see cref="Word"/> uses for a tile: the glyph, or the caption if wider.</summary>
        public static float WordWidth(Tile tile)
        {
            float w = Mathf.Max(GlyphPx + 4, FontBook.Width(tile?.Alien ?? "", 16) + 6);
            return Mathf.Ceil(w / 2f) * 2f;
        }

        /// <summary>Height of a <see cref="Word"/> cell: glyph, a pixel, then the caption.</summary>
        public const int WordH = GlyphPx + 1 + 16;

        static readonly HashSet<string> Small = new HashSet<string> { "the", "a", "an", "is", "are", "of", "for", "to", "in", "my", "it", "this" };

        /// <summary>The words of a meaning that carry it, if every one of them has a picture; otherwise null.</summary>
        static List<string> Pictured(string meaning)
        {
            var words = new List<string>();
            foreach (var raw in (meaning ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string word = raw.Trim('.', ',', '!', '?', ':', ';', '(', ')', '\'', '"', '\u2026').ToLowerInvariant();
                if (word.Length == 0 || Small.Contains(word)) continue;
                if (!Pictograms.Has(word)) return null;
                words.Add(word);
            }
            return words.Count > 0 && words.Count <= 4 ? words : null;
        }

        /// <summary>Width a plate wants for a meaning: one line if it fits in <paramref name="maxW"/>, otherwise two.</summary>
        public static float PlateWidth(string meaning, float maxW)
        {
            string label = (meaning ?? "").Trim().ToUpperInvariant();
            float one = Mathf.Ceil((FontBook.Width(label, 16) + 16) / 2f) * 2f;
            var pics = Pictured(meaning);
            if (pics != null && one <= maxW) return Mathf.Max(one, pics.Count * 30 + 12);
            if (one <= maxW) return one;
            for (float w = 112; w <= maxW; w += 8) if (Kit.Lines(label, 16, w - 14) <= 2) return w;
            return maxW;
        }

        /// <summary>
        /// The meaning of a phrase: carved pictures when every word has one, the words always. Long
        /// meanings ("The cat looks for the key.") wrap onto two lines.
        /// </summary>
        public static float Plate(Transform parent, float x, float y, float minW, float maxW, float h, string meaning, bool hot)
        {
            meaning = (meaning ?? "").Trim();
            string label = meaning.ToUpperInvariant();
            float w = Mathf.Clamp(PlateWidth(meaning, maxW), minW, maxW);
            Kit.Box("PlateFill", parent, hot ? Pal.RustDeep.A(0.55f) : Pal.Ash.A(0.6f), x + 1, y + 1, w - 2, h - 2);
            Kit.Frame("Plate", parent, hot ? Pal.Bone : Pal.Rust, x, y, w, h, hot);
            var ink = hot ? Pal.Bone : Pal.Bone.A(0.85f);
            var pics = Pictured(meaning);
            int lines = Kit.Lines(label, 16, w - 14);
            if (pics != null && lines <= 1 && h >= 42)
            {
                float ix = x + (w - (pics.Count * 26 + (pics.Count - 1) * 4)) / 2f;
                foreach (var word in pics)
                {
                    Kit.Pic("Pic", parent, Pictograms.Get(word), hot ? Pal.Bone : Pal.RustHot, ix, y + 2, 26, 26);
                    ix += 30;
                }
                Kit.Label("Gloss", parent, label, 16, ink, TextAnchor.MiddleCenter, x, y + h - 17, w, 16);
            }
            else if (lines <= 1) Kit.Label("Gloss", parent, label, 16, ink, TextAnchor.MiddleCenter, x, y + (h - 16) / 2f, w, 16);
            else
            {
                lines = Mathf.Min(lines, 2);
                var t = Kit.Label("Gloss", parent, label, 16, ink, TextAnchor.UpperCenter, x + 7, y + (h - lines * 18) / 2f - 3, w - 14, lines * 18 + 4, true);
                Kit.Pitch(t, 18);
            }
            return w;
        }

        /// <summary>Ship power: one cell per console, lit as each is decoded. Returns the width used.</summary>
        public static float Power(Transform parent, float x, float y, int solved, int total, Color lit, List<Image> cells = null)
        {
            total = Mathf.Clamp(total, 0, 12);
            Kit.Pic("Ship", parent, Pictograms.Get("ship"), lit, x, y + 1, 13, 13);
            float cx = x + 18;
            for (int i = 0; i < total; i++)
            {
                bool on = i < solved;
                Kit.Box("CellEdge", parent, on ? lit : lit.A(0.55f), cx, y, 7, 15);
                var cell = Kit.Box("Cell", parent, on ? lit : Pal.Void, cx + 1, y + 1, 5, 13);
                cells?.Add(cell);
                cx += 10;
            }
            return cx - 3 - x;
        }

        public static float PowerWidth(int total) => 18 + Mathf.Clamp(total, 0, 12) * 10 - 3;

        /// <summary>"ENTER > DO THE THING", blinking. Returns the node so it can be shown late.</summary>
        public static RectTransform Cue(Transform parent, float y, string text, Color c)
        {
            var node = Kit.Node("Cue", parent, 0, y, UiRig.StageW, 16);
            float tw = FontBook.Width(text, 16);
            float total = 13 + 8 + tw;
            float x = (UiRig.StageW - total) / 2f;
            Kit.Pic("Key", node, Pictograms.Get("enter"), c, x, 1, 13, 13);
            Kit.Label("Text", node, text, 16, c, TextAnchor.MiddleLeft, x + 21, 0, tw + 4, 16);
            return node;
        }
    }
}
