using System;
using Mirror;
using UnityEngine;

namespace HypeSwarm.Shared.Net
{
    /// <summary>
    /// The champion state that has to reach the other players but is not position — currently just
    /// facing, which position cannot imply because aim is independent of movement (spec §5.5.1).
    /// </summary>
    /// <remarks>
    /// Client-authoritative, like movement (spec §10). The owner writes; Mirror relays through the
    /// host to everyone else. In a PvE game a client lying about which way it is pointing costs its
    /// own lobby nothing, and buying that back with prediction and reconciliation would cost the
    /// thing the game is actually made of.
    ///
    /// <para>An angle, not a vector: half the bandwidth, and it cannot arrive denormalised. The
    /// owner's own reads come straight back out of the field, so local facing stays exactly as
    /// immediate as it was before any of this was networked.</para>
    /// </remarks>
    [AddComponentMenu("Hype Swarm/Champion Network State")]
    [DisallowMultipleComponent]
    public sealed class ChampionNetworkState : NetworkBehaviour
    {
        [SyncVar]
        float facingDegrees;

        void Awake()
        {
            // Set here rather than left to the inspector. A prefab that lost this value would still
            // run — the owner would look correct to itself and be frozen for everyone else, which is
            // close to the hardest kind of networking bug to notice.
            syncDirection = SyncDirection.ClientToServer;

            // Facing is what the other four players read to know where someone is pointing before
            // they cast. Sending it on every network tick rather than every 100ms is a few bytes.
            syncInterval = 0f;
        }

        /// <summary>Where this champion is facing, as a planar direction. Never zero.</summary>
        public Vector2 Facing
        {
            get
            {
                var radians = facingDegrees * Mathf.Deg2Rad;
                return new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
            }
        }

        /// <summary>Facing as a compass angle, clockwise from world +Z.</summary>
        public float FacingDegrees => facingDegrees;

        /// <summary>
        /// Called by the owner every frame with its current facing. Ignored on anyone else's
        /// champion — a client may only speak for the one it owns.
        /// </summary>
        public void SubmitFacing(Vector2 facing)
        {
            if (!isOwned || facing.sqrMagnitude <= 0f)
            {
                return;
            }

            facingDegrees = Mathf.Atan2(facing.x, facing.y) * Mathf.Rad2Deg;
        }

        // --- Dash ---------------------------------------------------------------------------

        /// <summary>
        /// A dash began on this champion, with its duration in seconds. Raised on every machine,
        /// including the one that started it.
        /// </summary>
        public event Action<float> DashStarted;

        /// <summary>The dash finished or was cancelled. Raised on every machine.</summary>
        public event Action DashEnded;

        /// <summary>
        /// Called by the owner when its motor starts a dash. Raises the event here immediately and
        /// tells everyone else.
        /// </summary>
        /// <remarks>
        /// The owner does not wait for the round trip. A client showing its own mobility ability
        /// only once the host has acknowledged it is a client that feels 80ms of lag on the input
        /// players touch most (spec §5.5.1, §12).
        ///
        /// <para>This is a placeholder shape, not the final one. When the ability system arrives
        /// (Phase 6) dashes will be effect steps emitting events through it, and this pair of calls
        /// goes away — but the split it demonstrates, logic deciding and presentation subscribing,
        /// is the one that has to hold from the start.</para>
        /// </remarks>
        public void SubmitDashStarted(float duration)
        {
            if (!isOwned)
            {
                return;
            }

            DashStarted?.Invoke(duration);
            CmdDashStarted(duration);
        }

        public void SubmitDashEnded()
        {
            if (!isOwned)
            {
                return;
            }

            DashEnded?.Invoke();
            CmdDashEnded();
        }

        [Command]
        void CmdDashStarted(float duration) => RpcDashStarted(duration);

        [Command]
        void CmdDashEnded() => RpcDashEnded();

        [ClientRpc(includeOwner = false)]
        void RpcDashStarted(float duration) => DashStarted?.Invoke(duration);

        [ClientRpc(includeOwner = false)]
        void RpcDashEnded() => DashEnded?.Invoke();
    }
}
