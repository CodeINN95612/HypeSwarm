using HypeSwarm.Shared.Net;
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
    /// <para>It subscribes to <see cref="ChampionNetworkState"/> rather than to the motor, which is
    /// what makes the other four players' dashes visible: the owner raises the event locally at the
    /// instant it dashes and the same event arrives on every other machine a moment later. Reading
    /// the motor instead would work perfectly on the one champion in the scene that has one.</para>
    ///
    /// <para>The compression is uniform rather than a stretch along the direction of travel. The
    /// visual is rotated to face the aim, and a dash goes where the player is moving, so a stretch
    /// applied on this transform would elongate the champion sideways whenever those two disagree —
    /// which is most of the time, and is the whole point of the control scheme. A real stretch needs
    /// its own transform aligned to the dash, and is worth doing when there is art to stretch.</para>
    /// </remarks>
    [RequireComponent(typeof(ChampionNetworkState))]
    public sealed class ChampionDashFeedback : MonoBehaviour
    {
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

        ChampionNetworkState state;
        Vector3 restScale = Vector3.one;
        float dashDuration;
        float dashRemaining;
        float recoveryRemaining;

        void Awake()
        {
            state = GetComponent<ChampionNetworkState>();

            if (visual != null)
            {
                restScale = visual.localScale;
            }
        }

        void OnEnable()
        {
            if (state == null)
            {
                return;
            }

            state.DashStarted += OnDashStarted;
            state.DashEnded += OnDashEnded;
        }

        void OnDisable()
        {
            if (state == null)
            {
                return;
            }

            state.DashStarted -= OnDashStarted;
            state.DashEnded -= OnDashEnded;

            ResetVisual();
        }

        void LateUpdate()
        {
            if (visual == null)
            {
                return;
            }

            if (dashRemaining > 0f)
            {
                // Eases out across the dash so the shape has settled by the time the champion is
                // under control again, rather than snapping back on the frame it ends.
                dashRemaining = Mathf.Max(0f, dashRemaining - Time.deltaTime);
                ApplyShape(dashDuration > 0f ? dashRemaining / dashDuration : 0f);
                return;
            }

            if (recoveryRemaining <= 0f)
            {
                return;
            }

            recoveryRemaining = Mathf.Max(0f, recoveryRemaining - Time.deltaTime);
            ApplyShape(recovery > 0f ? recoveryRemaining / recovery : 0f);
        }

        /// <summary>
        /// Runs the shape off a local clock started by the event, rather than polling the motor.
        /// A copy of another player's champion has no motor to poll, and a dash nobody else can see
        /// is a dash that reads as a teleport at the far end.
        /// </summary>
        void OnDashStarted(float duration)
        {
            dashDuration = Mathf.Max(0.01f, duration);
            dashRemaining = dashDuration;
            recoveryRemaining = 0f;

            if (trail != null)
            {
                trail.Clear();
                trail.emitting = true;
            }
        }

        void OnDashEnded()
        {
            dashRemaining = 0f;
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
            dashRemaining = 0f;
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
