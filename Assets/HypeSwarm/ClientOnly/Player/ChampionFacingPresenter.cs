using HypeSwarm.Shared.Net;
using HypeSwarm.Shared.Movement;
using UnityEngine;

namespace HypeSwarm.ClientOnly.Player
{
    /// <summary>
    /// Rotates the champion's visual to the facing carried by <see cref="ChampionNetworkState"/>.
    /// </summary>
    /// <remarks>
    /// Local and remote champions read the same field, so there is one facing path rather than two.
    /// The owner writes it and reads it back in the same frame — no smoothing, exactly as immediate
    /// as it was before movement was networked — while a copy smooths, because its updates arrive at
    /// the send rate rather than the frame rate and snapping between them would strobe.
    ///
    /// <para>Facing turns the visual and never the root. The root carries the collider and, later,
    /// every spatial query the horde makes against this champion (§11); rotating it would rotate a
    /// capsule that is meant to stay axis-aligned.</para>
    /// </remarks>
    [RequireComponent(typeof(ChampionNetworkState))]
    public sealed class ChampionFacingPresenter : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Child transform rotated to face the aim direction.")]
        Transform visual;

        [SerializeField]
        [Tooltip("Degrees per second a remote champion turns to catch up with its last known " +
                 "facing. High enough to look responsive, low enough to hide the send interval.")]
        float remoteTurnSpeed = 900f;

        ChampionNetworkState state;

        void Awake()
        {
            state = GetComponent<ChampionNetworkState>();
        }

        void LateUpdate()
        {
            if (visual == null || state == null)
            {
                return;
            }

            var facing = MotionPlane.ToWorld(state.Facing);

            if (facing.sqrMagnitude <= 0f)
            {
                return;
            }

            var target = Quaternion.LookRotation(facing, Vector3.up);

            visual.rotation = state.isOwned
                ? target
                : Quaternion.RotateTowards(visual.rotation, target, remoteTurnSpeed * Time.deltaTime);
        }
    }
}
