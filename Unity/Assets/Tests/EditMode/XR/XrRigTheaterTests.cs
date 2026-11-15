using System.Reflection;
using Fnmw.Core;
using NUnit.Framework;
using UnityEngine;

namespace Fnmw.XR.Tests
{
    /// <summary>
    /// The real rig, built in edit mode around a stand-in player, driven frame by frame: when a cutscene (or the
    /// death ending) takes over, the theater screen must come up in front of the face the headset reports, at the
    /// room pose the player is actually in (turned, walked sideways, body snap-turned).
    /// </summary>
    public class XrRigTheaterTests
    {
        GameObject _player, _rigGo;
        XrRig _rig;
        Transform _eye;

        static readonly MethodInfo UpdateM = typeof(XrRig).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly MethodInfo LateM = typeof(XrRig).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance);

        [SetUp]
        public void SetUp()
        {
            XrBridge.Reset();
            Services.ResetToFakes();
            XrBridge.Active = true;
            _player = new GameObject("Player");
            _player.AddComponent<CharacterController>();
            var cam = new GameObject("Main Camera").AddComponent<Camera>();
            cam.transform.SetParent(_player.transform, false);
            _rigGo = new GameObject("XrRig");
            _rig = _rigGo.AddComponent<XrRig>();
            _rig.Build(_player.transform, cam);
            _eye = cam.transform;
        }

        [TearDown]
        public void TearDown()
        {
            if (_rig) _rig.Shutdown();
            if (_rigGo) Object.DestroyImmediate(_rigGo);
            if (_player) Object.DestroyImmediate(_player);
            XrBridge.Reset();
            Services.ResetToFakes();
        }

        void Frame() { UpdateM.Invoke(_rig, null); LateM.Invoke(_rig, null); }

        Transform Screen => _rigGo.transform.Find("XrTheater/CameraOffset/TheaterScreen");
        Transform TheaterOffset => _rigGo.transform.Find("XrTheater/CameraOffset");

        /// <summary>Put the head at a tracking-space pose (what the headset reports), as the pose driver would.</summary>
        void HeadAt(Vector3 trackingPos, Quaternion trackingRot) { _eye.localPosition = trackingPos; _eye.localRotation = trackingRot; }

        void AssertScreenInFrontOf(Vector3 trackingPos, Quaternion trackingRot, string what)
        {
            var screen = Screen;
            Assert.IsNotNull(screen);
            Assert.IsTrue(screen.gameObject.activeInHierarchy, what + ": theater up");
            // the theater head is driven by the same tracking pose under the theater's offset
            Vector3 head = TheaterOffset.TransformPoint(trackingPos);
            Vector3 face = TheaterOffset.rotation * trackingRot * Vector3.forward; face.y = 0; face.Normalize();
            Vector3 to = screen.position - head; to.y = 0;
            Assert.AreEqual(TheaterLayout.Distance, to.magnitude, 0.02f, what + ": distance");
            Assert.Less(Vector3.Angle(face, to), 1f, what + ": straight ahead of the face");
            Assert.Greater(Vector3.Dot(screen.forward, to.normalized), 0.999f, what + ": facing the viewer");
            Assert.AreEqual(head.y, screen.position.y, 0.02f, what + ": eye level");
            Assert.AreEqual(XrRig.TheaterLayer, XrBridge.GlassLayer, "the UI glass moves to the theater with the view");
        }

        [Test]
        public void IntroTheater_FacesTheHead_TurnedAndWalkedSideways()
        {
            Services.State.SetPhase(GamePhase.Title);
            _player.transform.rotation = Quaternion.Euler(0, 45f, 0);          // two snap turns: the body has turned too
            var pos = new Vector3(1.3f, 1.72f, -0.9f); var rot = Quaternion.Euler(12f, 200f, 4f);
            HeadAt(pos, rot);
            Frame();
            Assert.IsFalse(Screen.gameObject.activeInHierarchy, "no theater before the cutscene");
            XrBridge.CutscenePlaying = true;
            Frame();
            AssertScreenInFrontOf(pos, rot, "intro");
        }

        [Test]
        public void DeathTheater_WaitsForTheInWorldScare_ThenFacesTheHeadAtThatMoment()
        {
            Services.State.SetPhase(GamePhase.Explore);
            HeadAt(new Vector3(0f, 1.7f, 0f), Quaternion.identity);
            Frame();
            Services.State.SetPhase(GamePhase.Dead);
            XrBridge.HoldTheater = true;                                  // DeathCutscene's in-world beat
            var pos = new Vector3(-0.7f, 1.65f, 1.1f); var rot = Quaternion.Euler(-5f, -95f, 0f);
            HeadAt(pos, rot);                                             // the player flinches to the side
            Frame();
            Assert.IsFalse(Screen.gameObject.activeInHierarchy, "the scare plays in the player's own view first");
            Assert.AreSame(_eye, XrBridge.Head);
            XrBridge.HoldTheater = false;
            Frame();
            AssertScreenInFrontOf(pos, rot, "death");
        }

        [Test]
        public void EachCutscene_RealignsToWhereverTheHeadIsThen()
        {
            Services.State.SetPhase(GamePhase.Title);
            HeadAt(new Vector3(0.2f, 1.7f, 0.3f), Quaternion.Euler(0, 30f, 0));
            Frame();
            XrBridge.CutscenePlaying = true; Frame();
            XrBridge.CutscenePlaying = false; Frame();
            Assert.IsFalse(Screen.gameObject.activeInHierarchy);
            var pos = new Vector3(-1.1f, 1.6f, 0.8f); var rot = Quaternion.Euler(0, 170f, 0);
            HeadAt(pos, rot);
            Frame();
            XrBridge.CutscenePlaying = true; Frame();
            AssertScreenInFrontOf(pos, rot, "second cutscene");
        }
    }
}
