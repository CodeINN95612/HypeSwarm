using HypeSwarm.Shared.Movement;
using UnityEngine;

namespace HypeSwarm.ClientOnly.Player
{
    /// <summary>
    /// Draws the dash: the champion compresses for its duration and leaves a trail behind them.
    /// </summary>
    /// <remarks>
    /// This component exists as much to establish the pattern as to look good. The motor decides a
    /// dash happened and says so; nothing about the trail, the stretch, or their timings is visible
    /// to it (§12). When the dash becomes a real mobility ability in Phase 2 the effect steps will
    /// emit events the same way, and this is the shape their presentation takes.
    ///
    /// <para>Everything it does is cosmetic and framed in terms of the dash's own duration, so
    /// retuning dash distance or speed does not leave the visual out of step with the movement.</para>
    ///
    /// <para>The compression is uniform rather than a stretch along the direction of travel. The
    /// visual is rotated to face the aim, and a dash goes where the player is moving, so a stretch
    /// applied on this transform would elongate the champion sideways whenever those two disagree —
    /// which is most of the time, and is the whole point of the control scheme. A real stretch needs
    /// its own transform aligned to the dash, and is worth doing when there is art to stretch.</para>
    /// </remarks>
    public sealed class ChampionDashFeedback : MonoBehaviour
    {
        [SerializeField]
        ChampionController champion;

        [SerializeField]
        [Tooltip("Transform to compress. Scaled, so give it its own object rather than sharing one " +
                 "with a collider.")]
        Transform visual;

        [SerializeField]
        [Tooltip("Optional trail, emitted only during the dash.")]
        TrailRenderer trail;

        [SerializeField]
        [Tooltip("Scale at the start of a dash, as a multiple of rest. Below one reads as bracing " +
                 "into the movement.")]
        float dashScale = 0.82f;

        [SerializeField]
        [Tooltip("Seconds to return to rest after the dash ends.")]
        float recovery = 0.12f;

        Vector3 restScale = Vector3.one;
        float recoveryRemaining;

        void Awake()
        {
            if (champion == null)
            {
                champion = GetComponentInParent<ChampionController>();
            }

            if (visual != null)
            {
                restScale = visual.localScale;
            }
        }

        void OnEnable()
        {
            if (champion == null)
            {
                return;
            }

            champion.Motor.DashStarted += OnDashStarted;
            champion.Motor.DashEnded += OnDashEnded;
        }

        void OnDisable()
        {
            if (champion == null)
            {
                return;
            }

            champion.Motor.DashStarted -= OnDashStarted;
            champion.Motor.DashEnded -= OnDashEnded;

            ResetVisual();
        }

        void LateUpdate()
        {
            if (visual == null || champion == null)
            {
                return;
            }

            var motor = champion.Motor;

            if (motor.IsDashing)
            {
                // Eases out across the dash so the shape has settled by the time the champion is
                // under control again, rather than snapping back on the frame it ends.
                ApplyShape(1f - motor.DashProgress);
                return;
            }

            if (recoveryRemaining <= 0f)
            {
                return;
            }

            recoveryRemaining = Mathf.Max(0f, recoveryRemaining - Time.deltaTime);
            ApplyShape(recovery > 0f ? recoveryRemaining / recovery : 0f);
        }

        void OnDashStarted(DashEvent dash)
        {
            recoveryRemaining = 0f;

            if (trail != null)
            {
                trail.Clear();
                trail.emitting = true;
            }
        }

        void OnDashEnded()
        {
            recoveryRemaining = recovery;

            if (trail != null)
            {
                trail.emitting = false;
            }
        }

        /// <summary>Blends between rest at zero and full compression at one.</summary>
        void ApplyShape(float amount)
        {
            amount = Mathf.Clamp01(amount);

            visual.localScale = restScale * Mathf.Lerp(1f, dashScale, amount);
        }

        void ResetVisual()
        {
            recoveryRemaining = 0f;

            if (visual != null)
            {
                visual.localScale = restScale;
            }

            if (trail != null)
            {
                trail.emitting = false;
            }
        }
    }
}
