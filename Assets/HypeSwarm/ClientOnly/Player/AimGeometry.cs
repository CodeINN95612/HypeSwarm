using UnityEngine;

namespace HypeSwarm.ClientOnly.Player
{
    /// <summary>
    /// The screen-to-ground arithmetic that sits between a mouse and the motor.
    /// </summary>
    /// <remarks>
    /// Small, pure, and separated from the components that use it because every function here has
    /// a degenerate case that produces a plausible-looking wrong answer rather than an exception:
    /// a ray that never meets the ground, a cursor exactly on the character, a camera yaw that
    /// silently inverts the controls. Those are cheap to test and unpleasant to debug through a
    /// viewport.
    /// </remarks>
    public static class AimGeometry
    {
        /// <summary>
        /// Intersects a ray with the horizontal plane at <paramref name="planeHeight"/>.
        /// </summary>
        /// <remarks>
        /// Returns false rather than a point when the ray runs parallel to the plane or points away
        /// from it — which happens whenever the camera is levelled or the cursor passes the horizon.
        /// The naive version divides by a near-zero denominator and hands back a point millions of
        /// units away; the aim direction derived from it is still a unit vector, so nothing looks
        /// broken until the character snaps to face the horizon.
        /// </remarks>
        public static bool TryGroundPoint(Ray ray, float planeHeight, out Vector3 point)
        {
            const float minimumDenominator = 1e-5f;

            var denominator = ray.direction.y;

            if (denominator > -minimumDenominator && denominator < minimumDenominator)
            {
                point = default;
                return false;
            }

            var distance = (planeHeight - ray.origin.y) / denominator;

            if (distance <= 0f)
            {
                point = default;
                return false;
            }

            point = ray.origin + ray.direction * distance;
            return true;
        }

        /// <summary>
        /// Rotates a planar input vector by the camera's yaw, so that "forward" means away from the
        /// viewer rather than along world +Z.
        /// </summary>
        /// <remarks>
        /// With the camera square to the world these are the same vector, which is exactly why this
        /// is easy to leave out and hard to notice: the controls only invert once someone yaws the
        /// rig to frame a stage differently, and by then it reads as a level bug.
        /// </remarks>
        public static Vector2 CameraRelative(Vector2 input, float cameraYawDegrees)
        {
            var radians = cameraYawDegrees * Mathf.Deg2Rad;
            var cos = Mathf.Cos(radians);
            var sin = Mathf.Sin(radians);

            return new Vector2(
                input.x * cos + input.y * sin,
                input.y * cos - input.x * sin);
        }

        /// <summary>
        /// How far the camera should bias toward what the player is aiming at, as a world offset.
        /// </summary>
        /// <remarks>
        /// This is the whole of "aim readability" as framing: pushing the view a bounded distance
        /// toward the cursor shows more of the space being aimed into without letting the character
        /// leave the frame. The clamp is the load-bearing part — unbounded, a cursor flicked to the
        /// screen edge throws the camera off the character entirely.
        /// </remarks>
        public static Vector3 LeadOffset(Vector3 from, Vector3 to, float fraction, float maxDistance)
        {
            if (fraction <= 0f || maxDistance <= 0f)
            {
                return Vector3.zero;
            }

            var offset = to - from;
            offset.y = 0f;
            offset *= Mathf.Clamp01(fraction);

            return Vector3.ClampMagnitude(offset, maxDistance);
        }

        /// <summary>
        /// The camera's position for a given focus point, derived from pitch, yaw, and distance
        /// rather than authored as an offset — so changing the angle in the inspector keeps the
        /// focus point centred instead of sliding the framing off it.
        /// </summary>
        public static Vector3 BoomPosition(Vector3 focus, float pitchDegrees, float yawDegrees, float distance)
        {
            var rotation = Quaternion.Euler(pitchDegrees, yawDegrees, 0f);
            return focus - rotation * Vector3.forward * Mathf.Max(0f, distance);
        }
    }
}
