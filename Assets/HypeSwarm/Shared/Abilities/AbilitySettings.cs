using HypeSwarm.Shared.Tuning;

namespace HypeSwarm.Shared.Abilities
{
    /// <summary>
    /// The ability numbers that belong to the system rather than to any one ability.
    /// </summary>
    /// <remarks>
    /// Balance and performance levers, so they live in the tuning file rather than in a compiled
    /// constant or an inspector field (§13.3). Read through <c>GameTuning.Abilities</c>.
    ///
    /// <para>No test pins these values. What has a right answer is the shape — that a passive pulses on
    /// an interval rather than every frame, that a slowed cast is slower than a free one and faster
    /// than a root — and that is tested where it is implemented, with values the test supplies.</para>
    /// </remarks>
    public readonly struct AbilitySettings
    {
        public const string TuningPrefix = "ability.";

        public const string PassiveTickIntervalKey = TuningPrefix + "passive_tick_interval";
        public const string SlowedCastSpeedKey = TuningPrefix + "slowed_cast_speed";
        public const string RootedCastCeilingKey = TuningPrefix + "rooted_cast_ceiling";

        public AbilitySettings(float passiveTickInterval, float slowedCastSpeed, float rootedCastCeiling)
        {
            PassiveTickInterval = passiveTickInterval;
            SlowedCastSpeed = slowedCastSpeed;
            RootedCastCeiling = rootedCastCeiling;
        }

        /// <summary>
        /// Seconds between passive pulses.
        /// </summary>
        /// <remarks>
        /// <b>A performance number, not a balance one.</b> Five continuous radius queries against two
        /// thousand enemies every frame is the cost §5.5.3 warns about, so passives run a few times a
        /// second instead. Passive effects are authored as rates and multiplied by this interval, which
        /// is what lets it be retuned for frame time without rebalancing every passive in the game.
        /// </remarks>
        public float PassiveTickInterval { get; }

        /// <summary>
        /// Share of top speed kept while casting a <see cref="CastCost.Slowed"/> ability.
        /// </summary>
        /// <remarks>
        /// The middle of the four costs, and the only one that is a number rather than a rule. Too high
        /// and a slowed cast is a free cast; too low and it is a root with extra steps — and the whole
        /// value of the axis is that the four are distinguishable while playing (§5.5.4).
        /// </remarks>
        public float SlowedCastSpeed { get; }

        /// <summary>
        /// Longest cast time a rooted ability may author, in seconds.
        /// </summary>
        /// <remarks>
        /// Not enforced at runtime — <see cref="AbilityValidation"/> reports it, because the authored
        /// number is the bug and silently shortening it would hide that. In a dense horde a long root
        /// is a death sentence rather than a decision, and the trap list names it.
        /// </remarks>
        public float RootedCastCeiling { get; }

        /// <summary>The compiled fallback, for when there is no config to read.</summary>
        public static AbilitySettings Default => new AbilitySettings(0.25f, 0.4f, 0.6f);

        public static AbilitySettings FromTuning(TuningConfig config)
        {
            if (config == null)
            {
                return Default;
            }

            var defaults = Default;

            return new AbilitySettings(
                config.GetFloat(PassiveTickIntervalKey, defaults.PassiveTickInterval),
                config.GetFloat(SlowedCastSpeedKey, defaults.SlowedCastSpeed),
                config.GetFloat(RootedCastCeilingKey, defaults.RootedCastCeiling));
        }

        /// <summary>Everything wrong with these, for the validation window. Null when clean.</summary>
        public string Validate()
        {
            if (PassiveTickInterval <= 0f)
            {
                return $"{PassiveTickIntervalKey} is {PassiveTickInterval}, which would stop every passive in the game.";
            }

            if (SlowedCastSpeed <= 0f || SlowedCastSpeed >= 1f)
            {
                return $"{SlowedCastSpeedKey} is {SlowedCastSpeed}; outside (0, 1) a slowed cast is either a root or a free cast.";
            }

            if (RootedCastCeiling <= 0f)
            {
                return $"{RootedCastCeilingKey} is {RootedCastCeiling}, so no rooted ability could ever pass validation.";
            }

            return null;
        }
    }
}
