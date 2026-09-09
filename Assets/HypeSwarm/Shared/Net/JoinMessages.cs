using Mirror;

namespace HypeSwarm.Shared.Net
{
    /// <summary>
    /// Sent by a client once it is connected, asking to be given a champion.
    /// </summary>
    /// <remarks>
    /// Mirror can create the player automatically, but it does so before the client has said
    /// anything about itself, which would mean spawning first and learning the name afterwards.
    /// Asking explicitly means the host knows who it is admitting at the moment it decides whether
    /// to admit them — the same shape a lobby with champion selection will need (Phase 4), so the
    /// handshake does not have to be reopened then.
    /// </remarks>
    public struct JoinRequestMessage : NetworkMessage
    {
        public string DisplayName;
    }

    /// <summary>The host's answer. Sent whether or not the join succeeded.</summary>
    public struct JoinResultMessage : NetworkMessage
    {
        public bool Accepted;

        /// <summary>Slot index in the lobby, or -1 when refused.</summary>
        public int SlotIndex;

        /// <summary>Why, when refused. Empty on success.</summary>
        public string Reason;
    }
}
