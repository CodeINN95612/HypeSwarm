namespace HypeSwarm.Shared.Abilities
{
    /// <summary>
    /// What an ability is for. Every champion has exactly one of each (§5.5.3).
    /// </summary>
    /// <remarks>
    /// <b>This is a role tag, not a slot index.</b> Abilities live in an indexed array
    /// (<see cref="AbilityBook"/>, §8.3) and the role is a property of the ability sitting in a slot
    /// — which is what makes "your third ability is replaced" a one-line change and what lets an
    /// augment say <i>"your mobility ability leaves a damaging trail"</i> without naming a champion.
    ///
    /// <para>An enum rather than a string tag for now. Phase 3 step 9 formalises tags across
    /// champions, abilities, items and augments; when it does, this becomes one tag among many and
    /// <see cref="AbilityRoles.Tag"/> is the name it will carry. It is an enum today because five
    /// fixed roles that every champion must have is a structural guarantee, and a typo in a string
    /// should not be able to produce a champion with two ultimates and no dash.</para>
    /// </remarks>
    public enum AbilityRole
    {
        /// <summary>Always-on identity. Not cast: it pulses on a fixed tick (§5.5.3).</summary>
        Passive = 0,

        /// <summary>The bread-and-butter damage ability.</summary>
        Primary = 1,

        /// <summary>Utility, control, or situational damage.</summary>
        Secondary = 2,

        /// <summary>Dash, blink or jump — and always something more than moving (§5.5.3).</summary>
        Mobility = 3,

        /// <summary>Long cooldown, high impact.</summary>
        Ultimate = 4
    }

    /// <summary>Facts about <see cref="AbilityRole"/> the enum cannot express.</summary>
    public static class AbilityRoles
    {
        /// <summary>How many roles there are, and therefore how many slots a champion has.</summary>
        public const int Count = 5;

        /// <summary>Every role, in slot order. The order abilities are authored and displayed in.</summary>
        public static readonly AbilityRole[] All =
        {
            AbilityRole.Passive,
            AbilityRole.Primary,
            AbilityRole.Secondary,
            AbilityRole.Mobility,
            AbilityRole.Ultimate
        };

        /// <summary>
        /// The tag name this role will carry once tags are formalised (Phase 3 step 9). Lowercase to
        /// match the content id rules, because that is what tags will be compared as.
        /// </summary>
        public static string Tag(AbilityRole role)
        {
            switch (role)
            {
                case AbilityRole.Passive: return "passive";
                case AbilityRole.Primary: return "primary";
                case AbilityRole.Secondary: return "secondary";
                case AbilityRole.Mobility: return "mobility";
                case AbilityRole.Ultimate: return "ultimate";
                default: return "unknown";
            }
        }

        /// <summary>
        /// Whether the player casts this, as opposed to it running itself. Only the passive is not
        /// cast, and the distinction is load-bearing: a passive has no cast time, spends no charge,
        /// and must never be reachable from an input binding.
        /// </summary>
        public static bool IsCast(AbilityRole role) => role != AbilityRole.Passive;
    }
}
