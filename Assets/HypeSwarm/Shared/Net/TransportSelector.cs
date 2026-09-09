namespace HypeSwarm.Shared.Net
{
    /// <summary>What is actually reachable from this process right now.</summary>
    public readonly struct TransportAvailability
    {
        /// <summary>True when a Steam client answered and the relay can be used.</summary>
        public bool SteamAvailable { get; }

        /// <summary>Why Steam is not available, for the log line and the dev panel. Empty when it is.</summary>
        public string SteamUnavailableReason { get; }

        public TransportAvailability(bool steamAvailable, string steamUnavailableReason = "")
        {
            SteamAvailable = steamAvailable;
            SteamUnavailableReason = steamAvailable ? string.Empty : steamUnavailableReason ?? string.Empty;
        }

        public static TransportAvailability DirectOnly(string reason) => new TransportAvailability(false, reason);

        public static TransportAvailability WithSteam() => new TransportAvailability(true);
    }

    /// <summary>The chosen transport and, always, why.</summary>
    public readonly struct TransportSelection
    {
        public TransportKind Kind { get; }

        /// <summary>A sentence fit for a log line. Never empty.</summary>
        public string Reason { get; }

        /// <summary>True when the caller asked for something other than what it got.</summary>
        public bool IsFallback { get; }

        public TransportSelection(TransportKind kind, string reason, bool isFallback = false)
        {
            Kind = kind;
            Reason = reason;
            IsFallback = isFallback;
        }

        public override string ToString() => $"{Kind} transport: {Reason}";
    }

    /// <summary>
    /// Decides which transport a process runs on (spec §13.4). Pure and total — every combination
    /// of request and environment produces a transport, because failing to choose one means failing
    /// to start.
    /// </summary>
    /// <remarks>
    /// Direct is the floor. KCP needs nothing but a socket, so there is always somewhere to fall
    /// back to, and this never returns "no transport".
    ///
    /// <para>The rule that earns this its own type is the headless one: a dedicated server has no
    /// Steam client attached, so a server launched with <c>-hypeswarm-transport steam</c> must be
    /// overruled rather than obeyed. Written inline at the call site that override is a two-line
    /// condition someone deletes while tidying; here it is a named rule with a test.</para>
    /// </remarks>
    public static class TransportSelector
    {
        public static TransportSelection Select(NetworkLaunchOptions options, TransportAvailability availability)
        {
            if (options == null)
            {
                return Select(null, availability, headless: false);
            }

            return Select(options.RequestedTransport, availability, options.Headless);
        }

        public static TransportSelection Select(
            TransportKind? requested,
            TransportAvailability availability,
            bool headless)
        {
            if (headless)
            {
                return new TransportSelection(
                    TransportKind.Direct,
                    requested == TransportKind.Steam
                        ? "a headless process has no Steam client to relay through, so the requested " +
                          "Steam transport was overruled (spec §13.4)"
                        : "a headless process serves over a direct connection (spec §13.4)",
                    isFallback: requested == TransportKind.Steam);
            }

            if (requested == TransportKind.Direct)
            {
                return new TransportSelection(TransportKind.Direct, "requested on the command line");
            }

            if (availability.SteamAvailable)
            {
                return new TransportSelection(
                    TransportKind.Steam,
                    requested == TransportKind.Steam
                        ? "requested on the command line"
                        : "a Steam client is running, so friends can be invited directly");
            }

            var why = string.IsNullOrEmpty(availability.SteamUnavailableReason)
                ? "no Steam client is available"
                : availability.SteamUnavailableReason;

            return new TransportSelection(
                TransportKind.Direct,
                requested == TransportKind.Steam
                    ? $"Steam was requested but is unusable: {why}"
                    : $"{why}, so connections are by address",
                isFallback: requested == TransportKind.Steam);
        }
    }
}
