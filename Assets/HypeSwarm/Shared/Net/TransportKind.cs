namespace HypeSwarm.Shared.Net
{
    /// <summary>
    /// The transports this game can run on. Chosen at runtime, never compiled in (spec §13.4).
    /// </summary>
    /// <remarks>
    /// There are exactly two because there are exactly two ways to reach a host: through a friend's
    /// Steam client, or at an address someone typed. A dedicated server on a VPS has no Steam client
    /// attached and cannot use the first, which is the whole reason this enum exists rather than a
    /// single hardcoded transport.
    /// </remarks>
    public enum TransportKind
    {
        /// <summary>KCP over UDP, addressed by host and port. Works anywhere, needs a reachable host.</summary>
        Direct = 0,

        /// <summary>Steam datagram relay via FizzySteamworks. Handles NAT and identity; needs a Steam client.</summary>
        Steam = 1
    }
}
