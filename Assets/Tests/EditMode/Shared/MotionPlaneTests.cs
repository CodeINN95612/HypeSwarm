using HypeSwarm.Shared.Movement;
using NUnit.Framework;
using UnityEngine;

namespace HypeSwarm.Shared.Tests
{
    [TestFixture]
    public sealed class MotionPlaneTests
    {
        [Test]
        public void PlanarY_IsWorldZ()
        {
            var world = MotionPlane.ToWorld(new Vector2(3f, 5f));

            Assert.That(world, Is.EqualTo(new Vector3(3f, 0f, 5f)),
                "the planar convention is (world x, world z); swapping it inverts every control");
        }

        [Test]
        public void FlattenAndToWorld_RoundTrip()
        {
            var planar = new Vector2(-2.5f, 7.25f);

            Assert.That(MotionPlane.Flatten(MotionPlane.ToWorld(planar)), Is.EqualTo(planar));
        }

        [Test]
        public void Flatten_DropsHeight()
        {
            Assert.That(MotionPlane.Flatten(new Vector3(1f, 99f, 2f)), Is.EqualTo(new Vector2(1f, 2f)));
        }

        [Test]
        public void DirectionBetween_IsAUnitVectorOnThePlane()
        {
            var direction = MotionPlane.DirectionBetween(Vector3.zero, new Vector3(3f, 12f, 4f));

            Assert.That(direction.magnitude, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(direction, Is.EqualTo(new Vector2(0.6f, 0.8f)).Using(new Vector2Comparer(0.0001f)),
                "the height difference must not tilt the direction");
        }

        /// <summary>
        /// The cursor sits exactly on the character every time the player passes under it. Normalising
        /// that difference produces NaN, NaN reaches the facing, the facing reaches the transform, and
        /// the character vanishes — with no error anywhere.
        /// </summary>
        [Test]
        public void DirectionBetween_CoincidentPoints_IsZeroRatherThanNaN()
        {
            var direction = MotionPlane.DirectionBetween(Vector3.one, Vector3.one);

            Assert.That(direction, Is.EqualTo(Vector2.zero));
            Assert.That(float.IsNaN(direction.x), Is.False);
        }

        [Test]
        public void DirectionBetween_PointsSeparatedOnlyInHeight_IsZero()
        {
            var direction = MotionPlane.DirectionBetween(Vector3.zero, new Vector3(0f, 10f, 0f));

            Assert.That(direction, Is.EqualTo(Vector2.zero));
        }

        sealed class Vector2Comparer : System.Collections.Generic.IEqualityComparer<Vector2>
        {
            readonly float tolerance;

            public Vector2Comparer(float tolerance)
            {
                this.tolerance = tolerance;
            }

            public bool Equals(Vector2 a, Vector2 b)
            {
                return Mathf.Abs(a.x - b.x) <= tolerance && Mathf.Abs(a.y - b.y) <= tolerance;
            }

            public int GetHashCode(Vector2 value) => value.GetHashCode();
        }
    }
}
