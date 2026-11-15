using System;
using UnityEngine;

namespace Fnmw.Core.Telemetry
{
    /// <summary>
    /// Head pose from a plain transform (the main camera by default). Works on desktop and in every build,
    /// and needs no XR package. Controllers are reported untracked, so the backend computes head features only.
    /// </summary>
    public sealed class CameraPoseSource : IQsPoseSource
    {
        readonly Func<Transform> _head;

        public CameraPoseSource(Func<Transform> head = null)
        {
            _head = head ?? (() => Camera.main != null ? Camera.main.transform : Services.Player);
        }

        public string Name => "camera";
        public bool HasControllers => false;

        public QsFrame Sample(double t)
        {
            var frame = new QsFrame { T = t, Head = QsPose.Untracked, Left = QsPose.Untracked, Right = QsPose.Untracked };
            var head = _head();
            if (head != null)
                frame.Head = new QsPose { Tracked = true, Position = head.position, Rotation = head.rotation };
            return frame;
        }
    }

#if FNMW_QS_XR
    /// <summary>
    /// Headset and Touch controllers through UnityEngine.XR.InputDevices (OpenXR on Quest). Compiled only with the
    /// FNMW_QS_XR scripting define, which the Quest branch sets, so this branch builds without any XR package.
    /// Velocity is read when the runtime reports it; the backend derives velocity from pose either way.
    /// </summary>
    public sealed class XrPoseSource : IQsPoseSource
    {
        UnityEngine.XR.InputDevice _head, _left, _right;

        public string Name => "xr";
        public bool HasControllers => true;

        public static bool Available()
        {
            var head = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.Head);
            return head.isValid;
        }

        static UnityEngine.XR.InputDevice Refresh(UnityEngine.XR.InputDevice d, UnityEngine.XR.XRNode node) =>
            d.isValid ? d : UnityEngine.XR.InputDevices.GetDeviceAtXRNode(node);

        static QsPose Read(UnityEngine.XR.InputDevice d)
        {
            if (!d.isValid) return QsPose.Untracked;
            var p = QsPose.Untracked;
            d.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out p.Tracked);
            bool hasPos = d.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out p.Position);
            bool hasRot = d.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out p.Rotation);
            p.HasVelocity = d.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceVelocity, out p.Velocity);
            p.Tracked = p.Tracked && hasPos && hasRot;
            return p;
        }

        public QsFrame Sample(double t)
        {
            _head = Refresh(_head, UnityEngine.XR.XRNode.Head);
            _left = Refresh(_left, UnityEngine.XR.XRNode.LeftHand);
            _right = Refresh(_right, UnityEngine.XR.XRNode.RightHand);
            return new QsFrame { T = t, Head = Read(_head), Left = Read(_left), Right = Read(_right) };
        }
    }
#endif
}
