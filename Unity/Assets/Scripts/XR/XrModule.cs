using System.Collections.Generic;
using Fnmw.Core;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;

namespace Fnmw.XR
{
    /// <summary>
    /// VR entry point (Meta Quest 2 and later, through OpenXR). Two calls from GameBootstrap:
    /// <see cref="Detect"/> before any module installs, so the others can see <see cref="XrBridge.Active"/>,
    /// and <see cref="Install"/> after the Game module has spawned the player, to build the VR rig around it.
    ///
    /// The switch is the XR runtime itself: the desktop build has no XR loader assigned for Standalone in
    /// XR Plug-in Management, so no XR display ever starts, Detect leaves Active false and Install does
    /// nothing. The Android (Quest) build initialises OpenXR on startup, so both turn on.
    /// </summary>
    public static class XrModule
    {
        /// <summary>Force VR off even when a headset is running (tests, recordings). -novr on the command line too.</summary>
        public static bool Disabled;

        public static bool Detect()
        {
            XrBridge.Reset();
            if (Disabled || HasArg("-novr")) return false;
            XrBridge.Active = XrRunning();
            if (XrBridge.Active) Debug.Log("[XR] headset detected: " + LoaderName());
            return XrBridge.Active;
        }

        public static void Install()
        {
            if (!XrBridge.Active) return;
            var player = Services.Player;
            var cam = player ? player.GetComponentInChildren<Camera>(true) : Camera.main;
            if (!player || !cam)
            {
                Debug.LogWarning("[XR] no player camera to build the VR rig on; staying on desktop controls");
                XrBridge.Reset();
                return;
            }
            // The world's prompts name keyboard keys; name the controller button instead.
            foreach (var it in Interactable.All)
                if (it && !string.IsNullOrEmpty(it.Prompt)) it.Prompt = it.Prompt.Replace("Press E", "Press A");
            var go = new GameObject("XrRig");
            go.AddComponent<XrRig>().Build(player, cam);
            Debug.Log("[XR] VR rig installed");
        }

        static bool XrRunning()
        {
            var gs = XRGeneralSettings.Instance;
            var mgr = gs ? gs.Manager : null;
            if (mgr != null && mgr.activeLoader != null) return true;
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            foreach (var d in displays) if (d.running) return true;
            return false;
        }

        static string LoaderName()
        {
            var gs = XRGeneralSettings.Instance;
            var l = gs && gs.Manager ? gs.Manager.activeLoader : null;
            return l ? l.name : "XR display";
        }

        static bool HasArg(string a)
        {
            foreach (var s in System.Environment.GetCommandLineArgs()) if (s == a) return true;
            return false;
        }
    }
}
