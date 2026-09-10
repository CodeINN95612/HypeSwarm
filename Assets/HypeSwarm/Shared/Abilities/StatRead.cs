namespace HypeSwarm.Shared.Abilities
{
    /// <summary>
    /// Which number an ability reads off a stat. The three are genuinely different designs (§5.6.5).
    /// </summary>
    public enum StatRead
    {
        /// <summary>
        /// The resolved value, base included. The usual choice — <i>damage scales with Damage</i>.
        /// </summary>
        Total = 0,

        /// <summary>
        /// Only what items, augments and growth added. <i>An ability whose damage scales with bonus
        /// max HP</i>: the champion's own starting health does not pay it, so the ability is worth
        /// nothing at minute zero and scales with the build rather than with the champion.
        /// </summary>
        Bonus = 1,

        /// <summary>
        /// The derived, bounded effect of a stat — the share of the way to its asymptote, in
        /// <c>(-1, 1)</c> (§5.6.2). What to read when a coefficient should saturate with the stat
        /// rather than grow with it: <i>a projectile count scaling with move speed</i> wants this,
        /// because the linear value is unbounded and the count would be too.
        /// </summary>
        Effect = 2
    }
}
