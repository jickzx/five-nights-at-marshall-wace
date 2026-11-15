using NUnit.Framework;
using UnityEngine;

namespace Fnmw.XR.Tests
{
    /// <summary>
    /// The theater screen (death, escape, intro) must come up straight in front of where the headset faces when
    /// the cutscene starts, wherever the player stands or faces in their physical room (tracking space).
    /// </summary>
    public class XrTheaterTests
    {
        const float Eye = 1.6f;

        static void AssertInFront(Vector3 headPos, Quaternion headRot)
        {
            TheaterLayout.Screen(headPos, headRot, Eye, out var pos, out var rot);
            Vector3 fwd = headRot * Vector3.forward; fwd.y = 0; fwd.Normalize();
            Vector3 to = pos - headPos;
            Vector3 flat = new Vector3(to.x, 0, to.z);
            Assert.AreEqual(TheaterLayout.Distance, flat.magnitude, 0.01f, "screen distance from the head");
            Assert.Less(Vector3.Angle(fwd, flat), 1f, "screen straight ahead of the face, not of the room");
            Assert.AreEqual(headPos.y, pos.y, 0.01f, "screen centre at eye level");
            // A Unity quad shows its face to a viewer looking along its +Z: the screen's +Z must point away from the head.
            Assert.Greater(Vector3.Dot(rot * Vector3.forward, flat.normalized), 0.999f, "screen faces the viewer");
            Assert.Less(Mathf.Abs((rot * Vector3.right).y), 1e-3f, "screen stays level (no roll)");
        }

        [Test] public void FacingRoomForward_AtRoomCentre() => AssertInFront(new Vector3(0, Eye, 0), Quaternion.identity);

        [Test] public void TurnedRight_InTheRoom() => AssertInFront(new Vector3(0, Eye, 0), Quaternion.Euler(0, 90f, 0));

        [Test] public void TurnedAround_AndWalkedSideways() =>
            AssertInFront(new Vector3(1.3f, Eye, -0.9f), Quaternion.Euler(0, 180f, 0));

        [Test] public void LookingDownAndTilted_AtAnAngle_Displaced() =>
            AssertInFront(new Vector3(-0.8f, Eye - 0.05f, 1.1f), Quaternion.Euler(35f, -130f, 12f));

        [Test] public void LookingStraightDown_StillUsesFacing() =>
            AssertInFront(new Vector3(0.4f, Eye, 0.2f), Quaternion.Euler(89.5f, 60f, 0));
    }
}
