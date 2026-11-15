using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fnmw.Core.Telemetry
{
    /// <summary>One device's pose at one instant, in Unity tracking space: metres, left-handed, y up.</summary>
    public struct QsPose
    {
        public bool Tracked;
        public Vector3 Position;
        public Quaternion Rotation;
        /// <summary>True when the runtime reported a velocity this frame (OpenXR fills it only when it can).</summary>
        public bool HasVelocity;
        public Vector3 Velocity;          // m/s

        public static readonly QsPose Untracked = new QsPose { Tracked = false, Rotation = Quaternion.identity };
    }

    /// <summary>Head and both controllers sampled together. T is seconds on the session clock.</summary>
    public struct QsFrame
    {
        public double T;
        public QsPose Head, Left, Right;
    }

    /// <summary>
    /// Where poses come from. <see cref="CameraPoseSource"/> works everywhere (head only); the XR adapter
    /// (<c>XrPoseSource</c>, compiled only with the FNMW_QS_XR define) reads the headset and Touch controllers.
    /// </summary>
    public interface IQsPoseSource
    {
        string Name { get; }
        /// <summary>False when the source cannot see controllers at all (desktop camera): hands are sent untracked.</summary>
        bool HasControllers { get; }
        QsFrame Sample(double t);
    }

    /// <summary>A discrete game event as the backend expects it (see qs-backend/docs/api.md).</summary>
    public sealed class QsEvent
    {
        public Guid Id = Guid.NewGuid();      // lets the backend drop a retried duplicate
        public double T;
        public string Type;
        public string QuestionKey;            // may be null
        /// <summary>Pre-built JSON object for the payload, or null. Built by <see cref="QsWire"/> helpers only.</summary>
        public string PayloadJson;
    }

    /// <summary>The frames of the last few seconds. Pure, so the windowing is testable without a scene.</summary>
    public sealed class QsFrameBuffer
    {
        readonly List<QsFrame> _frames = new List<QsFrame>();
        readonly double _keepSeconds;

        public QsFrameBuffer(double keepSeconds = 4.0) { _keepSeconds = Math.Max(1.0, keepSeconds); }

        public int Count => _frames.Count;

        public void Add(QsFrame f)
        {
            if (_frames.Count > 0 && f.T <= _frames[_frames.Count - 1].T) return;   // out-of-order or duplicate frame
            _frames.Add(f);
            double cutoff = f.T - _keepSeconds;
            int drop = 0;
            while (drop < _frames.Count && _frames[drop].T < cutoff) drop++;
            if (drop > 0) _frames.RemoveRange(0, drop);
        }

        /// <summary>Frames with tStart &lt;= T &lt; tEnd, oldest first.</summary>
        public List<QsFrame> Window(double tStart, double tEnd)
        {
            var w = new List<QsFrame>();
            foreach (var f in _frames)
                if (f.T >= tStart && f.T < tEnd) w.Add(f);
            return w;
        }

        public void Clear() => _frames.Clear();
    }
}
