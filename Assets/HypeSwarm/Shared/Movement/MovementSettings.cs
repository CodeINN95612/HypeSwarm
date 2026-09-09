using System;
using UnityEngine;

namespace HypeSwarm.Shared.Movement
{
    /// <summary>
    /// The tunable feel of a champion's movement and mobility ability.
    /// </summary>
    /// <remarks>
    /// Public mutable fields rather than the usual <c>[SerializeField] private</c> pairing, for two
    /// reasons. Tests build settings inline and vary one number at a time, and — more importantly —
    /// these numbers are found by dragging them in play mode, which is the whole method for
    /// Phase 1 step 2. Anything that makes that loop slower is the wrong trade here.
    ///
    /// <para>These are <b>not</b> in the external tuning config (§13.3) yet. That file is for
    /// difficulty curves and balance constants — values a designer changes without a build. Movement
    /// feel is found by watching it change under your hands, and the inspector is the tool for that.
    /// Move them out once they stop changing.</para>
    /// </remarks>
    [Serializable]
    public sealed class MovementSettings
    {
        [Header("Ground movement")]
        [Tooltip("Top planar speed, in metres per second.")]
        public float MaxSpeed = 7f;

        [Tooltip("Metres per second squared while an input is held. High values feel instant; " +
                 "low values feel like ice.")]
        public float Acceleration = 70f;

        [Tooltip("Metres per second squared with no input held. Usually higher than acceleration — " +
                 "a fast stop reads as responsive, a slow one reads as a bug.")]
        public float Deceleration = 90f;

        [Header("Aiming")]
        [Tooltip("Degrees per second the character turns toward the aim direction. Zero snaps " +
                 "instantly, which is what a mouse should do — a turn rate makes the crosshair and " +
                 "the character disagree, and the player believes the character.")]
        public float TurnSpeed;

        [Header("Mobility (§5.5.3)")]
        [Tooltip("Metres travelled by one dash.")]
        public float DashDistance = 6f;

        [Tooltip("Seconds the dash takes. Distance divided by duration is the dash speed, so " +
                 "shortening this makes the dash faster rather than shorter.")]
        public float DashDuration = 0.18f;

        [Tooltip("Charges available. Each recharges on its own timer, so spending two returns two " +
                 "one cooldown later rather than one after another.")]
        public int DashCharges = 2;

        [Tooltip("Seconds one spent charge takes to return.")]
        public float DashCooldown = 4f;

        [Tooltip("Seconds after a dash before another may start. Exists to stop one input being " +
                 "read as two — in a game where dashing is the survival tool, an accidental double " +
                 "dash reads as unresponsiveness (§5.5.3).")]
        public float DashLockout = 0.2f;

        [Tooltip("Fraction of top speed the character carries out of a dash. Zero drops them to a " +
                 "dead stop, which reads as hitting a wall.")]
        public float DashExitSpeedFactor = 1f;

        /// <summary>Speed a dash travels at, derived rather than authored.</summary>
        public float DashSpeed => DashDistance / Mathf.Max(DashDuration, MinDashDuration);

        /// <summary>
        /// Smallest dash duration the motor will simulate. Dash speed divides by the duration, so
        /// zero is not a slow dash — it is a division by zero and an infinite displacement.
        /// </summary>
        public const float MinDashDuration = 0.01f;

        /// <summary>
        /// Clamps every field into a range the motor can simulate. Called at the top of each step,
        /// so a value dragged to nonsense in play mode is survivable rather than fatal.
        /// </summary>
        public void Validate()
        {
            MaxSpeed = Mathf.Max(0f, MaxSpeed);
            Acceleration = Mathf.Max(0f, Acceleration);
            Deceleration = Mathf.Max(0f, Deceleration);
            TurnSpeed = Mathf.Max(0f, TurnSpeed);

            DashDistance = Mathf.Max(0f, DashDistance);
            DashDuration = Mathf.Max(MinDashDuration, DashDuration);
            DashCharges = Mathf.Max(0, DashCharges);
            DashCooldown = Mathf.Max(0f, DashCooldown);
            DashLockout = Mathf.Max(0f, DashLockout);
            DashExitSpeedFactor = Mathf.Clamp01(DashExitSpeedFactor);
        }

        public MovementSettings Clone()
        {
            return (MovementSettings)MemberwiseClone();
        }
    }
}
