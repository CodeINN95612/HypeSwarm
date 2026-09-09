using UnityEngine;

namespace HypeSwarm.Shared.Movement
{
    /// <summary>
    /// The one place that knows gameplay is simulated on a plane.
    /// </summary>
    /// <remarks>
    /// Movement, aiming, and the spatial hash (§11) all work in two dimensions; only presentation
    /// cares about height. A <see cref="Vector2"/> in this codebase is therefore always
    /// <c>(world x, world z)</c> — never <c>(x, y)</c>.
    ///
    /// <para>The conversion is three lines, which is exactly why it must not be written inline at
    /// twenty call sites. An axis swapped in one of them produces movement that is subtly wrong
    /// rather than obviously broken, and it is nearly invisible in a top-down view.</para>
    /// </remarks>
    public static class MotionPlane
    {
        /// <summary>Lifts a planar vector into world space, leaving height at zero.</summary>
        public static Vector3 ToWorld(Vector2 planar)
        {
            return new Vector3(planar.x, 0f, planar.y);
        }

        /// <summary>Lifts a planar vector into world space at a given height.</summary>
        public static Vector3 ToWorld(Vector2 planar, float height)
        {
            return new Vector3(planar.x, height, planar.y);
        }

        /// <summary>Drops the height component of a world vector.</summary>
        public static Vector2 Flatten(Vector3 world)
        {
            return new Vector2(world.x, world.z);
        }

        /// <summary>
        /// The planar direction from one world point to another, or <see cref="Vector2.zero"/> when
        /// they are within <paramref name="epsilon"/> of each other.
        /// </summary>
        /// <remarks>
        /// The zero case is the one that matters: normalising a near-zero vector yields either a
        /// zero vector or NaN depending on how it is done, and a NaN aim direction propagates into
        /// facing, then into the transform, and the character disappears.
        /// </remarks>
        public static Vector2 DirectionBetween(Vector3 from, Vector3 to, float epsilon = 0.0001f)
        {
            var delta = Flatten(to - from);
            return delta.sqrMagnitude <= epsilon * epsilon ? Vector2.zero : delta.normalized;
        }
    }
}
