using System.Collections.Generic;
using HypeSwarm.Shared.Tuning;

namespace HypeSwarm.Shared.Stats
{
    /// <summary>
    /// The table of stat definitions a <see cref="StatSheet"/> resolves against.
    /// </summary>
    /// <remarks>
    /// One instance is shared by every sheet in a session — it is immutable, so there is no reason
    /// for a champion and a trash mob to each carry their own copy of eleven definitions.
    ///
    /// <para>The compiled defaults are the bottom layer. <see cref="FromTuning"/> puts the shipped
    /// config file on top (§13.3), so the numbers below are what the game falls back to rather than
    /// what it runs on, and changing a soft cap does not need a rebuild.</para>
    ///
    /// <para><b>The values here are balance, not behaviour.</b> No test pins them; tests cover the
    /// shape of the curve and the operation order, which are the things that have a right answer.</para>
    /// </remarks>
    public sealed class StatCatalog
    {
        /// <summary>Prefix for every stat tuning key: <c>stat.haste.soft_cap</c>.</summary>
        public const string TuningPrefix = "stat.";

        readonly StatDefinition[] definitions = new StatDefinition[StatIds.Count];

        /// <param name="entries">
        /// One definition per stat. Missing ones keep the compiled default rather than leaving a
        /// hole, so a partial catalog is usable and <see cref="Validate"/> is what complains.
        /// </param>
        public StatCatalog(IEnumerable<StatDefinition> entries)
        {
            var defaults = Defaults();

            for (var i = 0; i < definitions.Length; i++)
            {
                definitions[i] = defaults[i];
            }

            if (entries == null)
            {
                return;
            }

            foreach (var entry in entries)
            {
                var index = (int)entry.Id;

                if (index >= 0 && index < definitions.Length)
                {
                    definitions[index] = entry;
                }
            }
        }

        /// <summary>The compiled defaults. What a sheet uses when nobody supplied a catalog.</summary>
        public static StatCatalog Default { get; } = new StatCatalog(null);

        public StatDefinition this[StatId stat] => definitions[(int)stat];

        public IReadOnlyList<StatDefinition> Definitions => definitions;

        /// <summary>
        /// The defaults with every value the tuning config defines laid over them.
        /// </summary>
        /// <remarks>
        /// Every key is read unconditionally, so a stat missing from the config is recorded in
        /// <see cref="TuningConfig.MissingKeys"/> and shows up in the validation window. That is
        /// deliberate: a stat whose base value quietly came from a compiled constant while everything
        /// around it came from the file is exactly the discrepancy nobody finds by reading.
        /// </remarks>
        public static StatCatalog FromTuning(TuningConfig config)
        {
            if (config == null)
            {
                return Default;
            }

            var entries = new List<StatDefinition>(StatIds.Count);

            foreach (var definition in Default.Definitions)
            {
                var baseValue = config.GetFloat(BaseKey(definition), definition.BaseValue);

                var softCap = definition.IsDerived
                    ? config.GetFloat(SoftCapKey(definition), definition.SoftCap)
                    : definition.SoftCap;

                entries.Add(definition.With(baseValue, softCap));
            }

            return new StatCatalog(entries);
        }

        public static string BaseKey(StatDefinition definition) => TuningPrefix + definition.Key + ".base";

        public static string SoftCapKey(StatDefinition definition) => TuningPrefix + definition.Key + ".soft_cap";

        /// <summary>
        /// Everything wrong with this catalog. Empty is the only acceptable shipping state, and the
        /// validation window is where it is read.
        /// </summary>
        public IReadOnlyList<string> Validate()
        {
            var problems = new List<string>();
            var keys = new HashSet<string>();

            for (var i = 0; i < definitions.Length; i++)
            {
                var definition = definitions[i];

                if ((int)definition.Id != i)
                {
                    problems.Add($"Stat slot {i} holds the definition for {definition.Id}.");
                }

                if (string.IsNullOrEmpty(definition.Key))
                {
                    problems.Add($"Stat {definition.Id} has no tuning key.");
                }
                else if (!keys.Add(definition.Key))
                {
                    problems.Add($"Two stats share the tuning key '{definition.Key}'.");
                }

                if (definition.IsDerived && definition.SoftCap < StatCurve.MinimumSoftCap)
                {
                    problems.Add(
                        $"Stat {definition.Id} derives asymptotically but its soft cap is " +
                        $"{definition.SoftCap}, so one point of it would be worth the whole curve.");
                }

                if (definition.BaseValue < definition.Minimum)
                {
                    problems.Add(
                        $"Stat {definition.Id} starts at {definition.BaseValue}, below its own " +
                        $"minimum of {definition.Minimum}.");
                }
            }

            return problems;
        }

        /// <summary>
        /// The shipped starting numbers. Soft caps are all the same for now on purpose — one
        /// readable curve until there is content to tell them apart.
        /// </summary>
        static StatDefinition[] Defaults()
        {
            const float standardSoftCap = 100f;

            return new[]
            {
                new StatDefinition(StatId.MaxHealth, "max_health", "Max Health", StatKind.Linear, 100f, 0f, 1f),
                new StatDefinition(StatId.HealthRegen, "health_regen", "Health Regen", StatKind.Linear, 1f, 0f, 0f),
                new StatDefinition(StatId.Damage, "damage", "Damage", StatKind.Linear, 10f, 0f, 0f),
                new StatDefinition(StatId.Penetration, "penetration", "Penetration", StatKind.Linear, 0f, 0f, 0f),
                new StatDefinition(StatId.DamageReduction, "damage_reduction", "Damage Reduction", StatKind.Asymptotic, 0f, standardSoftCap),
                new StatDefinition(StatId.Haste, "haste", "Haste", StatKind.Asymptotic, 0f, standardSoftCap),
                new StatDefinition(StatId.MoveSpeed, "move_speed", "Move Speed", StatKind.Asymptotic, 0f, standardSoftCap),
                new StatDefinition(StatId.Dodge, "dodge", "Dodge", StatKind.Asymptotic, 0f, standardSoftCap),
                new StatDefinition(StatId.Lifesteal, "lifesteal", "Lifesteal", StatKind.Asymptotic, 0f, standardSoftCap),
                new StatDefinition(StatId.SlowResistance, "slow_resistance", "Slow Resistance", StatKind.Asymptotic, 0f, standardSoftCap),
                new StatDefinition(StatId.PickupRadius, "pickup_radius", "Pickup Radius", StatKind.Linear, 3f, 0f, 0f)
            };
        }
    }
}
