using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fnmw.Core.Fakes
{
    /// <summary>
    /// No network, no speech. H shows the engine's authored hints (level 1, then 2, then repeats 2);
    /// pronunciation is reported unavailable. Replaced by Scripts/Voice.
    /// </summary>
    public class FakeVoiceDirector : IVoiceDirector
    {
        readonly Dictionary<int, int> _level = new Dictionary<int, int>();

        public bool HintsAvailable
        {
            get
            {
                var s = Services.State;
                var world = s?.World;
                if (world == null || world.Puzzles == null || world.Puzzles.Count == 0) return false;
                int i = Mathf.Clamp(s.CurrentPuzzleIndex, 0, world.Puzzles.Count - 1);
                try { return Services.Language.GetHintContext(world, world.Puzzles[i].Id, null) != null; }
                catch (Exception) { return false; }
            }
        }

        public void RequestHint(int puzzleId)
        {
            var world = Services.State?.World;
            HintContext ctx = null;
            try { ctx = Services.Language.GetHintContext(world, puzzleId, null); } catch (Exception e) { Debug.LogException(e); }
            if (ctx?.OfflineHints == null || ctx.OfflineHints.Length == 0) return;
            _level.TryGetValue(puzzleId, out int lv);
            lv = Mathf.Min(lv + 1, ctx.OfflineHints.Length);
            _level[puzzleId] = lv;
            Services.UI.ShowHint(new HintView { PuzzleId = puzzleId, Visible = true, Text = ctx.OfflineHints[lv - 1], Level = lv, Source = VoiceSource.Offline });
        }

        public void RequestPronunciation(int puzzleId, bool replay) =>
            Services.UI.ShowPronunciation(new PronunciationView { PuzzleId = puzzleId, State = PronunciationState.Unavailable, Source = VoiceSource.Offline });
    }
}
