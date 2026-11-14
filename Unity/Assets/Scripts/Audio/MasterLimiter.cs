using UnityEngine;

namespace Fnmw.Audio
{
    /// <summary>
    /// Sits on the AudioListener and keeps the final mix (score + every 3D source) under full scale.
    /// Linear below the threshold, so it is transparent in normal play; a fast peak follower plus a soft knee
    /// catch the moments when a shriek, a stinger and the chase all land together.
    /// </summary>
    public sealed class MasterLimiter : MonoBehaviour
    {
        const float Threshold = 0.8f, Ceiling = 0.97f;
        float _env, _att, _rel;
        int _sr;

        void Awake()
        {
            _sr = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            _att = Mathf.Exp(-1f / (0.0005f * _sr));
            _rel = Mathf.Exp(-1f / (0.15f * _sr));
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            for (int i = 0; i < data.Length; i += channels)
            {
                float peak = 0f;
                for (int c = 0; c < channels; c++) { float a = Mathf.Abs(data[i + c]); if (a > peak) peak = a; }
                _env = peak > _env ? peak + (_env - peak) * _att : peak + (_env - peak) * _rel;
                float g = _env > Threshold ? Threshold / _env : 1f;
                for (int c = 0; c < channels; c++) data[i + c] = Knee(data[i + c] * g);
            }
        }

        /// <summary>Identity below the threshold, smoothly saturating to the ceiling above it.</summary>
        public static float Knee(float x)
        {
            float a = Mathf.Abs(x);
            if (a <= Threshold) return x;
            float span = Ceiling - Threshold;
            float y = Threshold + span * (float)System.Math.Tanh((a - Threshold) / span);
            return x < 0f ? -y : y;
        }

        /// <summary>Attach to the scene's AudioListener once it exists (the Game thread creates it with the player).</summary>
        public static MasterLimiter Ensure()
        {
            var listener = FindAnyObjectByType<AudioListener>();
            if (listener == null) return null;
            return listener.GetComponent<MasterLimiter>() ?? listener.gameObject.AddComponent<MasterLimiter>();
        }
    }
}
