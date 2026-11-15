using System.Collections.Generic;
using Fnmw.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Fnmw.UI
{
    /// <summary>
    /// Spoken lines while roaming: one line over a dark box, above SHIP POWER, with a speaker tag. On the console
    /// the same text goes to the console's footer instead (PuzzleScreen), so it never covers the tiles or slots.
    /// Lines are shown only while their audio actually plays; mock audio is labelled as a tone, not speech.
    /// </summary>
    internal sealed class SubtitleLayer
    {
        public const float Y = 284, H = 24, X = 30, W = 580;
        readonly RectTransform _root;
        readonly Text _text;
        string _shown;

        public SubtitleLayer(Transform stage)
        {
            _root = Kit.Node("Subtitle", stage, X, Y, W, H);
            Kit.Box("Back", _root, Pal.Void.A(0.78f), 0, 0, W, H);
            _text = Kit.Label("Text", _root, "", 16, Pal.Bone, TextAnchor.MiddleCenter, 8, 4, W - 16, 16);
            _root.gameObject.SetActive(false);
        }

        public RectTransform Root => _root;
        public string Shown => _shown;

        /// <summary>The tag and text exactly as drawn, e.g. "IT: YOU SLIPPED." or "IT (MOCK TONE, NOT SPEECH): ...".</summary>
        public static string Format(SubtitleLine line)
        {
            if (string.IsNullOrWhiteSpace(line.Text)) return null;
            string who = line.Channel == VoiceChannel.Monster ? "IT" : line.Channel == VoiceChannel.Ship ? "SHIP" : "VOICE";
            if (line.Source == VoiceSource.Mock) who += " (MOCK TONE, NOT SPEECH)";
            return (who + ": " + line.Text.Trim()).ToUpperInvariant();
        }

        /// <summary>Splits text into pages that fit <paramref name="lines"/> lines of <paramref name="width"/> at 16 px.</summary>
        public static List<string> Pages(string text, float width, int lines)
        {
            var pages = new List<string>();
            if (string.IsNullOrEmpty(text)) return pages;
            var words = text.Split(' ');
            string page = "";
            foreach (var w in words)
            {
                string next = page.Length == 0 ? w : page + " " + w;
                if (page.Length > 0 && Kit.Lines(next, 16, width) > lines) { pages.Add(page); page = w; }
                else page = next;
            }
            if (page.Length > 0) pages.Add(page);
            return pages;
        }

        public void Tick(string text, float t)
        {
            bool on = !string.IsNullOrEmpty(text);
            if (_root.gameObject.activeSelf != on) _root.gameObject.SetActive(on);
            if (!on) { _shown = null; return; }
            var pages = Pages(text, W - 16, 1);
            string page = pages.Count == 0 ? "" : pages[Mathf.Min(pages.Count - 1, (int)(t / 3.5f) % pages.Count)];
            if (page != _shown) { _shown = page; Kit.SetText(_text, page); }
        }
    }
}
