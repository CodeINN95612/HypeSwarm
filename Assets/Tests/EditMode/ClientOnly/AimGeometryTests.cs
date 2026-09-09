using HypeSwarm.ClientOnly.Player;
using NUnit.Framework;
using UnityEngine;

namespace HypeSwarm.ClientOnly.Tests
{
    /// <summary>
    /// Everything here is arithmetic with a degenerate case, and in every one of those cases the
    /// wrong answer is a plausible number rather than an exception — which is why they are worth
    /// four lines each.
    /// </summary>
    [TestFixture]
    public sealed class AimGeometryTests
    {
        const float Tolerance = 0.0001f;

        // --- Screen to ground --------------------------------------------------------------

        [Test]
        public void GroundPoint_LiesOnTheRequestedPlane()
        {
            var ray = new Ray(new Vector3(0f, 10f, -10f), new Vector3(0f, -1f, 1f).normalized);

            Assert.That(AimGeometry.TryGroundPoint(ray, 0f, out var point), Is.True);
            Assert.That(point.y, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(point.z, Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void GroundPoint_RespectsANonZeroPlaneHeight()
        {
            var ray = new Ray(new Vector3(0f, 10f, 0f), Vector3.down);

            Assert.That(AimGeometry.TryGroundPoint(ray, 1.5f, out var point), Is.True);
            Assert.That(point.y, Is.EqualTo(1.5f).Within(Tolerance));
        }

        /// <summary>
        /// A level camera, or a cursor above the horizon. The naive version divides by a denominator
        /// near zero and hands back a point kilometres away; normalised into an aim direction it is
        /// still a unit vector, so the character just faces the horizon and nothing reports a fault.
        /// </summary>
        [Test]
        public void GroundPoint_IsRefusedForARayParallelToThePlane()
        {
            var ray = new Ray(new Vector3(0f, 10f, 0f), Vector3.forward);

            Assert.That(AimGeometry.TryGroundPoint(ray, 0f, out _), Is.False);
        }

        [Test]
        public void GroundPoint_IsRefusedWhenThePlaneIsBehindTheRay()
        {
            var ray = new Ray(new Vector3(0f, 10f, 0f), Vector3.up);

            Assert.That(AimGeometry.TryGroundPoint(ray, 0f, out _), Is.False);
        }

        // --- Camera-relative input ---------------------------------------------------------

        [Test]
        public void CameraRelative_IsIdentityWhenTheCameraIsSquareToTheWorld()
        {
            var input = new Vector2(0.3f, -0.7f);

            Assert.That(AimGeometry.CameraRelative(input, 0f).x, Is.EqualTo(input.x).Within(Tolerance));
            Assert.That(AimGeometry.CameraRelative(input, 0f).y, Is.EqualTo(input.y).Within(Tolerance));
        }

        /// <summary>
        /// Yaw the rig ninety degrees and "forward" must become world +X. Skipping this rotation is
        /// invisible until someone re-frames a stage, at which point the controls are simply wrong
        /// and it reads as a level bug rather than a camera one.
        /// </summary>
        [Test]
        public void CameraRelative_RotatesForwardToMatchTheCamera()
        {
            var forward = AimGeometry.CameraRelative(Vector2.up, 90f);

            Assert.That(forward.x, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(forward.y, Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void CameraRelative_PreservesMagnitude()
        {
            var input = new Vector2(0.6f, 0.8f);

            Assert.That(AimGeometry.CameraRelative(input, 37f).magnitude, Is.EqualTo(1f).Within(Tolerance));
        }

        // --- Camera lead --------------------------------------------------------------------

        /// <summary>
        /// The clamp is the whole reason this function exists. Unbounded, a cursor flicked to the
        /// edge of a wide monitor throws the view off the character in a game where losing sight of
        /// yourself for half a second is fatal.
        /// </summary>
        [Test]
        public void LeadOffset_IsClampedToTheMaximum()
        {
            var offset = AimGeometry.LeadOffset(Vector3.zero, new Vector3(1000f, 0f, 0f), 0.5f, 5f);

            Assert.That(offset.magnitude, Is.EqualTo(5f).Within(Tolerance));
        }

        [Test]
        public void LeadOffset_IsAFractionOfTheDistanceBelowTheMaximum()
        {
            var offset = AimGeometry.LeadOffset(Vector3.zero, new Vector3(4f, 0f, 0f), 0.5f, 5f);

            Assert.That(offset.x, Is.EqualTo(2f).Within(Tolerance));
        }

        [Test]
        public void LeadOffset_IsZeroWhenTheCursorIsOnTheCharacter()
        {
            Assert.That(AimGeometry.LeadOffset(Vector3.one, Vector3.one, 0.5f, 5f), Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void LeadOffset_IgnoresHeight()
        {
            var offset = AimGeometry.LeadOffset(Vector3.zero, new Vector3(0f, 50f, 0f), 1f, 5f);

            Assert.That(offset, Is.EqualTo(Vector3.zero), "a height difference must not raise the camera");
        }

        [Test]
        public void LeadOffset_IsDisabledByAZeroFractionOrMaximum()
        {
            var far = new Vector3(10f, 0f, 0f);

            Assert.That(AimGeometry.LeadOffset(Vector3.zero, far, 0f, 5f), Is.EqualTo(Vector3.zero));
            Assert.That(AimGeometry.LeadOffset(Vector3.zero, far, 0.5f, 0f), Is.EqualTo(Vector3.zero));
        }

        // --- Boom ---------------------------------------------------------------------------

        [Test]
        public void BoomPosition_IsTheAuthoredDistanceFromTheFocus()
        {
            var focus = new Vector3(5f, 0f, -3f);

            var position = AimGeometry.BoomPosition(focus, 55f, 20f, 18f);

            Assert.That(Vector3.Distance(position, focus), Is.EqualTo(18f).Within(0.001f));
        }

        [Test]
        public void BoomPosition_IsAboveAndBehindTheFocus_ForAPositivePitch()
        {
            var position = AimGeometry.BoomPosition(Vector3.zero, 55f, 0f, 18f);

            Assert.That(position.y, Is.GreaterThan(0f));
            Assert.That(position.z, Is.LessThan(0f));
        }
    }
}
