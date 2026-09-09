using System;

namespace HypeSwarm.Shared.Combat
{
    /// <summary>
    /// What kind of damage a packet is, and the two mitigation steps it is allowed to skip.
    /// </summary>
    /// <remarks>
    /// <b>These are not damage schools.</b> There is one damage stat and one damage reduction stat
    /// (§5.6.1); nothing here changes which resistance applies, because there is only one. The first
    /// six values are the <i>Source axis</i> — how the damage was delivered — which is what carries
    /// build differentiation now that the physical/magical split is gone. "Projectile build" and
    /// "summon build" are the interesting distinction; "physical build" was only ever a number swap.
    ///
    /// <para><b>These become tags in Phase 3 step 9.</b> The tag system is the real home for
    /// provenance, and when it lands this enum should be backed by tags rather than living beside
    /// them. It exists now because a damage packet that does not record where it came from cannot be
    /// retrofitted later without revisiting every call site that ever created one, and there are
    /// about to be a lot of those.</para>
    /// </remarks>
    [Flags]
    public enum DamageFlags
    {
        None = 0,

        // --- Source axis (§5.6.1) ---

        /// <summary>Dealt by an ability cast.</summary>
        Ability = 1 << 0,

        /// <summary>Dealt by something that travelled.</summary>
        Projectile = 1 << 1,

        /// <summary>A tick of something applied earlier.</summary>
        DamageOverTime = 1 << 2,

        /// <summary>Dealt by a pet or other owned entity.</summary>
        Summon = 1 << 3,

        /// <summary>Dealt by standing in something.</summary>
        Zone = 1 << 4,

        /// <summary>Dealt by a triggered effect rather than a direct cast.</summary>
        Proc = 1 << 5,

        // --- Pipeline behaviour ---

        /// <summary>
        /// Skips damage reduction entirely. The "true damage" case, and how an execute or a
        /// percentage-of-max-health effect gets to mean what it says.
        /// </summary>
        IgnoresReduction = 1 << 8,

        /// <summary>
        /// Cannot be dodged. Damage over time and zone ticks want this — rolling dodge on every tick
        /// of a burn turns a defensive stat into a coin flip nobody can read.
        /// </summary>
        Undodgeable = 1 << 9
    }
}
