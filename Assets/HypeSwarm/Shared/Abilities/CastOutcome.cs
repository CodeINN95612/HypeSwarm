namespace HypeSwarm.Shared.Abilities
{
    /// <summary>What happened when something tried to cast.</summary>
    /// <remarks>
    /// A reason rather than a bool, because each of these has a different right response: the HUD
    /// flashes a cooldown, a held input retries next frame, and a refusal the host sends back has to be
    /// told apart from one the client decided for itself.
    /// </remarks>
    public enum CastOutcome
    {
        /// <summary>The cast began. It may already have resolved, if the cast time was zero.</summary>
        Started = 0,

        /// <summary>No ability in that slot. A binding pointing at a champion that does not have one.</summary>
        UnknownSlot = 1,

        /// <summary>
        /// A passive. It runs itself on a tick and is never cast, so reaching it from an input binding
        /// is a wiring mistake worth naming rather than silently ignoring.
        /// </summary>
        NotCastable = 2,

        /// <summary>Something else is mid-cast. One at a time, which is what makes a root a cost.</summary>
        Busy = 3,

        /// <summary>On cooldown, out of charges, or inside the lockout between two charges.</summary>
        OnCooldown = 4,

        /// <summary>Dead. Checked by the caller, which is the only thing that knows.</summary>
        Dead = 5
    }
}
