using HypeSwarm.Shared.Tuning;

namespace HypeSwarm.Shared.Combat
{
    /// <summary>
    /// The combat numbers that are not stats: how regeneration is gated, and how much health a revive
    /// comes back with.
    /// </summary>
    /// <remarks>
    /// Balance, so it lives in the tuning file rather than in a compiled constant or an inspector
    /// field (§13.3). Read through <c>GameTuning.Combat</c>, never constructed at a call site.
    ///
    /// <para>No test pins these values. The shape — a delay that gates regeneration, a fraction that
    /// bounds a revive — is what has a right answer, and that is tested on
    /// <see cref="HealthPool"/> with values the test supplies itself.</para>
    /// </remarks>
    public readonly struct CombatSettings
    {
        public const string TuningPrefix = "combat.";

        public const string RegenDelayKey = TuningPrefix + "regen_delay";
        public const string ReviveHealthFractionKey = TuningPrefix + "revive_health_fraction";

        public CombatSettings(float regenDelay, float reviveHealthFraction)
        {
            RegenDelay = regenDelay;
            ReviveHealthFraction = reviveHealthFraction;
        }

        /// <summary>
        /// Seconds of not being hit before health regeneration resumes.
        /// </summary>
        /// <remarks>
        /// The gate is what stops regeneration being a stat you can stand in the swarm and win with.
        /// It also gives kiting a payoff beyond not dying, which is the point of §5.5.4 — the space
        /// you make with a dash is space that heals you.
        /// </remarks>
        public float RegenDelay { get; }

        /// <summary>Share of maximum health a revived player comes back at.</summary>
        public float ReviveHealthFraction { get; }

        /// <summary>The compiled fallback, for when there is no config to read.</summary>
        public static CombatSettings Default => new CombatSettings(5f, 0.5f);

        public static CombatSettings FromTuning(TuningConfig config)
        {
            if (config == null)
            {
                return Default;
            }

            var defaults = Default;

            return new CombatSettings(
                config.GetFloat(RegenDelayKey, defaults.RegenDelay),
                config.GetFloat(ReviveHealthFractionKey, defaults.ReviveHealthFraction));
        }

        /// <summary>Everything wrong with these, for the validation window.</summary>
        public string Validate()
        {
            if (RegenDelay < 0f)
            {
                return $"{RegenDelayKey} is {RegenDelay}, and a negative delay is not a delay.";
            }

            if (ReviveHealthFraction <= 0f || ReviveHealthFraction > 1f)
            {
                return $"{ReviveHealthFractionKey} is {ReviveHealthFraction}, outside the share of maximum health a revive can return.";
            }

            return null;
        }
    }
}
