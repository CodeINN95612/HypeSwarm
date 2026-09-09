using UnityEngine;

namespace HypeSwarm.Shared.Movement
{
    /// <summary>
    /// Emitted when a dash begins. Carries everything presentation needs to draw it and nothing it
    /// could use to change it.
    /// </summary>
    /// <remarks>
    /// The motor does not spawn a trail, tilt the camera, or play a whoosh — it says a dash started
    /// and lets subscribers decide (§12). That is what allows the same code to run on a headless
    /// host, and what will let a client play its own dash instantly while the host confirms
    /// (§5.5.1).
    /// </remarks>
    public readonly struct DashEvent
    {
        /// <summary>Planar unit direction the dash travels.</summary>
        public readonly Vector2 Direction;

        /// <summary>Metres the dash will cover.</summary>
        public readonly float Distance;

        /// <summary>Seconds the dash will take.</summary>
        public readonly float Duration;

        /// <summary>Charges left after this one was spent.</summary>
        public readonly int ChargesRemaining;

        public DashEvent(Vector2 direction, float distance, float duration, int chargesRemaining)
        {
            Direction = direction;
            Distance = distance;
            Duration = duration;
            ChargesRemaining = chargesRemaining;
        }
    }
}
