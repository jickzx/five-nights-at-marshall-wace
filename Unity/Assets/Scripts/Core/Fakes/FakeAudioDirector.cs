using UnityEngine;

namespace Fnmw.Core.Fakes
{
    /// <summary>Logs only. Replaced by Scripts/Audio.</summary>
    public class FakeAudioDirector : IAudioDirector
    {
        float _last = -1f;
        public void SetIntensity(float i)
        {
            // Log only when the band changes so the console stays readable.
            int band = i < 0.33f ? 0 : i < 0.66f ? 1 : 2;
            int lastBand = _last < 0 ? -1 : _last < 0.33f ? 0 : _last < 0.66f ? 1 : 2;
            if (band != lastBand) Debug.Log($"[FakeAudio] intensity band -> {(band == 0 ? "unsettling" : band == 1 ? "tense" : "panic")}");
            _last = i;
        }
        public void PlayStinger(string name) => Debug.Log($"[FakeAudio] stinger: {name}");
    }
}
