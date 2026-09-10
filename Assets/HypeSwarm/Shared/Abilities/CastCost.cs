namespace HypeSwarm.Shared.Abilities
{
    /// <summary>
    /// What using an ability costs in movement. The axis that makes kiting exist (§5.5.4).
    /// </summary>
    /// <remarks>
    /// With no basic attacks, this is the only thing an ability costs besides its cooldown, and it is
    /// the mechanism for both kiting and role differentiation. Kiting becomes the decision of
    /// <i>when to pay the root</i>; a support whose kit is mostly free-cast can contribute while
    /// permanently repositioning, and a burst champion pays for damage with a vulnerability window.
    /// Neither needs a role label — the cast cost profile <i>is</i> the role.
    ///
    /// <para><b>Authored per ability from the start</b>, because it is tagged and therefore
    /// augment-modifiable: <i>"your rooted abilities may be cast while moving"</i> is a run-defining
    /// augment that works across the whole roster, and it is only expressible if every ability
    /// already declares what it costs. Retrofitting this would mean revisiting every ability.</para>
    ///
    /// <para>Two rules are enforced by <see cref="AbilityValidation"/> rather than left to authoring
    /// discipline: rooted casts stay short, and mobility abilities are always free — a mobility slot
    /// that rooted you could not serve its escape function.</para>
    /// </remarks>
    public enum CastCost
    {
        /// <summary>No interruption; cast at full speed. Sustain, shields, utility.</summary>
        Free = 0,

        /// <summary>Movement continues, slower. Sustained channels and beams.</summary>
        Slowed = 1,

        /// <summary>Movement stops for the cast. Burst damage and hard control — and kept short.</summary>
        Rooted = 2,

        /// <summary>
        /// Movement stops and the effect builds while held, released by the player or cut short.
        /// </summary>
        /// <remarks>
        /// The richest of the four, because the player chooses how much to commit under pressure. An
        /// ability that heals for damage taken while channelling inverts the usual risk — the horde
        /// closing in is what makes the channel better.
        /// </remarks>
        Channelled = 3
    }

    /// <summary>How a <see cref="CastCost"/> behaves, in one place so nothing re-derives it.</summary>
    public static class CastCosts
    {
        /// <summary>Whether the player holds the button and decides when to let go.</summary>
        public static bool IsHeld(CastCost cost) => cost == CastCost.Channelled;

        /// <summary>
        /// What top speed is multiplied by while this is being cast.
        /// </summary>
        /// <param name="slowedSpeed">
        /// The tuned share of speed a slowed cast keeps (<see cref="AbilitySettings.SlowedCastSpeed"/>).
        /// Passed in rather than read, so this stays a pure function and the motor stays free of
        /// tuning lookups.
        /// </param>
        public static float SpeedMultiplier(CastCost cost, float slowedSpeed)
        {
            switch (cost)
            {
                case CastCost.Free:
                    return 1f;

                case CastCost.Slowed:
                    return slowedSpeed < 0f ? 0f : slowedSpeed;

                default:
                    // Rooted and channelled both stand still. Zero rather than a small number: a
                    // root that let you drift is a root the player cannot feel or plan around.
                    return 0f;
            }
        }
    }
}
