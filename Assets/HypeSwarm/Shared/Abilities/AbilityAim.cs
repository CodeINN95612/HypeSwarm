using HypeSwarm.Shared.Movement;
using UnityEngine;

namespace HypeSwarm.Shared.Abilities
{
    /// <summary>
    /// Where a cast is pointed: a ground point and the planar direction to it.
    /// </summary>
    /// <remarks>
    /// Both, rather than one derived from the other at the point of use. A cone wants the direction, a
    /// zone wants the point, and several abilities want both — recomputing either from the other at
    /// every step would mean two steps in one cast could disagree about where it was aimed, which for
    /// an aimed game is the one inconsistency a player would notice.
    ///
    /// <para>This crosses the wire as the point alone. The direction is derived from the caster
    /// position the host already has, so sending it would be sending something the host can compute
    /// and must not trust.</para>
    /// </remarks>
    public readonly struct AbilityAim
    {
        public AbilityAim(Vector3 point, Vector2 direction)
        {
            Point = point;
            Direction = direction.sqrMagnitude > 0f ? direction.normalized : Vector2.zero;
        }

        /// <summary>The ground point being aimed at.</summary>
        public Vector3 Point { get; }

        /// <summary>Planar unit vector from the caster toward the point, or zero when there is none.</summary>
        public Vector2 Direction { get; }

        /// <summary>Whether there is a usable direction. False when aiming at your own feet.</summary>
        public bool HasDirection => Direction.sqrMagnitude > 0f;

        /// <summary>
        /// Aim from a caster at a point, deriving the direction. <paramref name="fallback"/> is used
        /// when the point is on top of the caster — the champion facing, normally, so aiming at your
        /// own feet points a cone forwards instead of nowhere.
        /// </summary>
        public static AbilityAim FromPoint(Vector3 casterPosition, Vector3 point, Vector2 fallback = default)
        {
            var direction = MotionPlane.DirectionBetween(casterPosition, point);

            if (direction.sqrMagnitude <= 0f)
            {
                direction = fallback;
            }

            return new AbilityAim(point, direction);
        }

        /// <summary>
        /// Aim in a direction with no meaningful point, for abilities that only care which way they
        /// are facing.
        /// </summary>
        public static AbilityAim FromDirection(Vector3 casterPosition, Vector2 direction)
        {
            return new AbilityAim(casterPosition + MotionPlane.ToWorld(direction), direction);
        }

        /// <summary>
        /// The same aim with the point pulled inside the ability's range.
        /// </summary>
        /// <remarks>
        /// <b>The host calls this too, on the point a client sent.</b> Ranges are authored (§5.5.5), so
        /// a point beyond one is either a bad frame or a client casting across the map; clamping is the
        /// right answer to both, and refusing the cast outright would punish the honest case.
        /// </remarks>
        public AbilityAim ClampedTo(Vector3 casterPosition, float range)
        {
            if (range <= 0f)
            {
                return this;
            }

            var offset = MotionPlane.Flatten(Point - casterPosition);

            if (offset.sqrMagnitude <= range * range)
            {
                return this;
            }

            var clamped = casterPosition + MotionPlane.ToWorld(offset.normalized * range);

            return new AbilityAim(new Vector3(clamped.x, Point.y, clamped.z), Direction);
        }
    }
}
