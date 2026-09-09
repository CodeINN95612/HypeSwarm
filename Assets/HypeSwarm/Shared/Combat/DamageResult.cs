namespace HypeSwarm.Shared.Combat
{
    /// <summary>
    /// What the stat layer did to a packet, before anything was subtracted from a health pool.
    /// </summary>
    /// <remarks>
    /// Split out from <see cref="DamageResult"/> so the arithmetic that reads stat sheets is a pure
    /// function with no pool, no entity, and nothing to mock. The pool consumes this and knows
    /// nothing about reduction or dodging.
    /// </remarks>
    public readonly struct DamageMitigation
    {
        public DamageMitigation(float requested, float amount, bool dodged, float reductionFraction)
        {
            Requested = requested;
            Amount = amount;
            Dodged = dodged;
            ReductionFraction = reductionFraction;
        }

        /// <summary>What the packet asked for.</summary>
        public float Requested { get; }

        /// <summary>What survived dodge and reduction.</summary>
        public float Amount { get; }

        public bool Dodged { get; }

        /// <summary>
        /// The share removed by damage reduction, in <c>(-1, 1)</c>. Negative when the defender
        /// reduction is negative, which is amplification rather than mitigation.
        /// </summary>
        public float ReductionFraction { get; }

        /// <summary>A packet that was avoided outright.</summary>
        public static DamageMitigation Avoided(float requested) => new DamageMitigation(requested, 0f, true, 0f);

        /// <summary>A packet nothing happened to.</summary>
        public static DamageMitigation Unmitigated(float requested) => new DamageMitigation(requested, requested, false, 0f);
    }

    /// <summary>
    /// What actually happened to a target. Returned by every damage call, and the input to lifesteal,
    /// on-hit triggers, damage numbers, and the kill credit that has not been written yet.
    /// </summary>
    public readonly struct DamageResult
    {
        public DamageResult(
            float requested,
            float mitigated,
            float absorbedByShield,
            float dealtToHealth,
            float overkill,
            bool dodged,
            bool killed)
        {
            Requested = requested;
            Mitigated = mitigated;
            AbsorbedByShield = absorbedByShield;
            DealtToHealth = dealtToHealth;
            Overkill = overkill;
            Dodged = dodged;
            Killed = killed;
        }

        /// <summary>What the packet asked for, before anything touched it.</summary>
        public float Requested { get; }

        /// <summary>What survived dodge and damage reduction.</summary>
        public float Mitigated { get; }

        public float AbsorbedByShield { get; }

        public float DealtToHealth { get; }

        /// <summary>
        /// Mitigated damage that landed on a target with less health than that left. Excluded from
        /// <see cref="Dealt"/> so an overkill for a thousand does not heal a lifesteal build to full.
        /// </summary>
        public float Overkill { get; }

        public bool Dodged { get; }

        /// <summary>This packet is what took the target to zero. True exactly once per death.</summary>
        public bool Killed { get; }

        /// <summary>
        /// Damage that landed, shields included and overkill excluded. What lifesteal and every
        /// damage-dealt trigger should read.
        /// </summary>
        public float Dealt => AbsorbedByShield + DealtToHealth;

        /// <summary>Nothing happened — no target, or a target already dead.</summary>
        public static DamageResult None => default;

        public override string ToString()
        {
            return Dodged
                ? $"dodged {Requested:0.##}"
                : $"{Dealt:0.##} dealt ({AbsorbedByShield:0.##} to shield) of {Requested:0.##}{(Killed ? ", killed" : string.Empty)}";
        }
    }
}
