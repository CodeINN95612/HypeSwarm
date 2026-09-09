namespace HypeSwarm.Shared.Stats
{
    /// <summary>How a stat turns into an effect.</summary>
    public enum StatKind
    {
        /// <summary>
        /// The number is the effect. Health, damage, regen, pickup radius — nothing breaks when
        /// these grow without bound, so nothing needs to bend them.
        /// </summary>
        Linear = 0,

        /// <summary>
        /// Stored linear, effect derived through <see cref="StatCurve"/> (§5.6.2). Everything that
        /// would break the game at 100%.
        /// </summary>
        Asymptotic = 1
    }

    /// <summary>
    /// What one stat is: its identity, how it reads, where it starts, and how it derives.
    /// </summary>
    /// <remarks>
    /// Data only. A definition never knows what any ability does with the stat — abilities read
    /// stats, stats never reach back (§5.6.5) — and it carries no tags, because paths are clusters
    /// of items and augments, not of numbers (§6.1).
    /// </remarks>
    public readonly struct StatDefinition
    {
        public StatDefinition(
            StatId id,
            string key,
            string displayName,
            StatKind kind,
            float baseValue,
            float softCap = 0f,
            float minimum = float.NegativeInfinity)
        {
            Id = id;
            Key = key;
            DisplayName = displayName;
            Kind = kind;
            BaseValue = baseValue;
            SoftCap = softCap;
            Minimum = minimum;
        }

        public StatId Id { get; }

        /// <summary>
        /// Snake-case name used in tuning keys — <c>stat.damage_reduction.soft_cap</c>. Separate
        /// from the enum name so renaming a stat in code does not silently orphan a config line.
        /// </summary>
        public string Key { get; }

        /// <summary>What a player sees. Not localised yet; that is Phase 5 work.</summary>
        public string DisplayName { get; }

        public StatKind Kind { get; }

        /// <summary>Where the stat starts before any modifier, growth, or item.</summary>
        public float BaseValue { get; }

        /// <summary>
        /// The K in <c>value / (value + K)</c>, and therefore the amount of this stat that buys half
        /// its asymptote. Meaningless for a linear stat.
        /// </summary>
        public float SoftCap { get; }

        /// <summary>
        /// Floor applied after every modifier resolves, or negative infinity for no floor. This is
        /// not a cap in the §5.6.2 sense — it is the line below which a value stops meaning
        /// anything, like zero max health or a negative pickup radius.
        /// </summary>
        public float Minimum { get; }

        public bool IsDerived => Kind == StatKind.Asymptotic;

        /// <summary>The same definition with different numbers. How a tuning layer is applied.</summary>
        public StatDefinition With(float baseValue, float softCap)
        {
            return new StatDefinition(Id, Key, DisplayName, Kind, baseValue, softCap, Minimum);
        }

        public override string ToString() => $"{DisplayName} ({Key})";
    }
}
