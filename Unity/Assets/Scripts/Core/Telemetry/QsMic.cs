using System;
using UnityEngine;

namespace Fnmw.Core.Telemetry
{
    /// <summary>
    /// The headset microphone, read only to compute breathing features (<see cref="QsBreath"/>). Started only after the
    /// candidate accepts a consent statement that names the microphone, and only when qs.json allows it.
    ///
    /// Audio stays in a one-second looping clip in memory; each frame the new samples are copied out, folded into the
    /// 20 ms envelope and dropped. Nothing is written to disk and no sample is ever sent.
    ///
    /// On Android (Quest) the RECORD_AUDIO permission is requested at runtime when the mic is first started; the
    /// manifest entry is added at build time (Unity adds it for any app using <see cref="Microphone"/>, and
    /// QsAndroidManifest in Fnmw.XR.Editor makes sure). If the person refuses, motion is still sent and audio is not.
    /// </summary>
    public sealed class QsMic
    {
        public const int SampleRate = 16000;
        const int ClipSeconds = 1;

        AudioClip _clip;
        string _device;
        int _readPos;
        float[] _buf = new float[4096];
#pragma warning disable CS0414   // read only in Android builds
        bool _permissionAsked;
#pragma warning restore CS0414
        public QsBreath Breath { get; private set; } = new QsBreath(SampleRate);

        public bool Running => _clip != null;
        public string Status { get; private set; } = "off";

        /// <summary>Ask for permission if needed and start recording into the loop. Call every frame until Running.</summary>
        public void TryStart()
        {
            if (_clip != null) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                if (!_permissionAsked)
                {
                    _permissionAsked = true;
                    Status = "asking for microphone permission";
                    UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
                }
                return;
            }
#endif
            _permissionAsked = true;
            if (Microphone.devices == null || Microphone.devices.Length == 0) { Status = "no microphone"; return; }
            _device = Microphone.devices[0];
            Microphone.GetDeviceCaps(_device, out int minF, out int maxF);
            // 16 kHz is plenty for a breath envelope; use the nearest rate the device supports (0,0 = any rate)
            int rate = (minF == 0 && maxF == 0) ? SampleRate : Mathf.Clamp(SampleRate, minF, maxF);
            _clip = Microphone.Start(_device, true, ClipSeconds, rate);
            _readPos = 0;
            Breath = new QsBreath(rate);
            Status = _clip != null ? "recording (breathing features only)" : "microphone failed to start";
        }

        /// <summary>True when the permission was asked and refused (Android); the sender then stops trying.</summary>
        public bool Refused
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return _permissionAsked && !UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone);
#else
                return false;
#endif
            }
        }

        /// <summary>Move the new samples from the loop into the breathing envelope. Call once per frame.</summary>
        public void Pump()
        {
            if (_clip == null) return;
            int pos = Microphone.GetPosition(_device);
            if (pos < 0 || pos == _readPos) return;
            int total = _clip.samples;
            int count = pos > _readPos ? pos - _readPos : total - _readPos + pos;
            while (count > 0)
            {
                int n = Math.Min(count, _buf.Length);
                _clip.GetData(_buf, _readPos);   // fills the buffer from _readPos, wrapping round the loop; first n are new
                Breath.Push(_buf, n);
                _readPos = (_readPos + n) % total;
                count -= n;
            }
            Array.Clear(_buf, 0, _buf.Length);   // no audio left lying around in the managed heap
        }

        public void Stop()
        {
            if (_clip != null) { Microphone.End(_device); UnityEngine.Object.Destroy(_clip); }
            _clip = null;
            Breath.Clear();
            Status = "off";
        }
    }
}
