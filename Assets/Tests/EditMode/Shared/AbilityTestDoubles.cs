using System;
using System.Collections.Generic;
using System.Reflection;
using HypeSwarm.Shared.Abilities;
using HypeSwarm.Shared.Combat;
using HypeSwarm.Shared.Content;
using HypeSwarm.Shared.Stats;
using HypeSwarm.Shared.Tuning;
using UnityEngine;

namespace HypeSwarm.Shared.Tests
{
    /// <summary>
    /// Plain C# stand-ins for the things an ability needs: a definition, a combatant, a world.
    /// </summary>
    /// <remarks>
    /// The whole reason the ability system is written against interfaces rather than asset types. Every
    /// rule about cooldowns, cast costs, targeting and scaling is exercised here with no ScriptableObject
    /// to author, no prefab, no scene, and no network — which is what makes the tests run in milliseconds
    /// and what makes a failure point at one thing.
    /// </remarks>
    public sealed class TestAbility : IAbilityDefinition
    {
        public TestAbility(string id = "ability.test", AbilityRole role = AbilityRole.Primary)
        {
            Id = ContentId.Parse(id);
            Role = role;
        }

        public ContentId Id { get; }

        public string DisplayName { get; set; } = "Test";

        public AbilityRole Role { get; set; }

        public CastCost CastCost { get; set; } = CastCost.Free;

        public float CastTime { get; set; }

        public float MaxChannelDuration { get; set; }

        public float Cooldown { get; set; } = 10f;

        public int Charges { get; set; } = 1;

        public float ChargeLockout { get; set; }

        public TargetingMode Targeting { get; set; } = TargetingMode.AimPoint;

        public float Range { get; set; } = 8f;

        public List<IEffectStep> StartStepList { get; } = new List<IEffectStep>();

        public List<IEffectStep> StepList { get; } = new List<IEffectStep>();

        public IReadOnlyList<IEffectStep> StartSteps => StartStepList;

        public IReadOnlyList<IEffectStep> Steps => StepList;
    }

    /// <summary>A champion with whatever abilities a test hands it.</summary>
    public sealed class TestChampion : IChampionDefinition
    {
        public TestChampion(string id = "champion.test", params IAbilityDefinition[] abilities)
        {
            Id = ContentId.Parse(id);
            AbilityList = new List<IAbilityDefinition>(abilities);
        }

        public ContentId Id { get; }

        public string DisplayName { get; set; } = "Test";

        public List<IAbilityDefinition> AbilityList { get; }

        public IReadOnlyList<IAbilityDefinition> Abilities => AbilityList;

        /// <summary>One of each role, all valid, as a base for tests that then break one thing.</summary>
        public static TestChampion Complete()
        {
            return new TestChampion(
                "champion.complete",
                new TestAbility("ability.passive", AbilityRole.Passive) { Cooldown = 0f },
                new TestAbility("ability.primary", AbilityRole.Primary),
                new TestAbility("ability.secondary", AbilityRole.Secondary),
                new TestAbility("ability.mobility", AbilityRole.Mobility),
                new TestAbility("ability.ultimate", AbilityRole.Ultimate));
        }
    }

    /// <summary>
    /// A combatant that records what was done to it, with a real <see cref="StatSheet"/> behind it.
    /// </summary>
    public sealed class TestCombatant : ICombatant
    {
        readonly HealthPool pool;

        public TestCombatant(
            Faction faction = Faction.Enemies,
            Vector3 position = default,
            float maxHealth = 1000f,
            uint id = 1u)
        {
            Faction = faction;
            Position = position;
            Id = id;
            Stats = new StatSheet();
            pool = new HealthPool(maxHealth);
        }

        public uint Id { get; }

        public Faction Faction { get; set; }

        public Vector3 Position { get; set; }

        public StatSheet Stats { get; }

        public bool IsDead => pool.IsDead;

        public float Current => pool.Current;

        public float Shield => pool.Shield;

        /// <summary>Every packet that reached this, in order. What a damage test asserts against.</summary>
        public List<DamagePacket> Hits { get; } = new List<DamagePacket>();

        public List<float> Heals { get; } = new List<float>();

        public List<StatModifier> Applied { get; } = new List<StatModifier>();

        public float LastShieldAmount { get; private set; }

        public float LastShieldDuration { get; private set; }

        /// <summary>Total requested damage, before any mitigation. The number an ability authored.</summary>
        public float TotalRequested
        {
            get
            {
                var total = 0f;

                for (var i = 0; i < Hits.Count; i++)
                {
                    total += Hits[i].Amount;
                }

                return total;
            }
        }

        public DamageResult TakeDamage(in DamagePacket packet)
        {
            Hits.Add(packet);

            // Unmitigated: what mitigation does to a packet is DamagePipelineTests' subject, not this.
            return pool.Apply(DamageMitigation.Unmitigated(packet.Amount));
        }

        public float Heal(float amount)
        {
            Heals.Add(amount);

            return pool.Heal(amount);
        }

        public bool AddShield(ModifierSource source, float amount, float duration)
        {
            LastShieldAmount = amount;
            LastShieldDuration = duration;

            return pool.Shields.Add(source, amount, duration);
        }

        public bool ApplyModifiers(ModifierSource source, float duration, StatModifier[] modifiers)
        {
            if (modifiers == null)
            {
                return false;
            }

            for (var i = 0; i < modifiers.Length; i++)
            {
                Applied.Add(modifiers[i]);
                Stats.Add(modifiers[i]);
            }

            return true;
        }

        public void Kill() => pool.Kill();
    }

    /// <summary>A world holding exactly what a test put in it.</summary>
    public sealed class TestWorld : IAbilityWorld
    {
        readonly CombatantWorld world = new CombatantWorld();

        public TestWorld(params ICombatant[] combatants)
        {
            for (var i = 0; i < combatants.Length; i++)
            {
                world.Register(combatants[i]);
            }
        }

        /// <summary>How many queries have been run. Proves a step asked exactly once.</summary>
        public int Queries { get; private set; }

        public void Add(ICombatant combatant) => world.Register(combatant);

        public int FindTargets(in TargetQuery query, List<ICombatant> into)
        {
            Queries++;

            return world.FindTargets(query, into);
        }
    }

    /// <summary>An effect step that records that it ran, for tests about ordering and about where.</summary>
    public sealed class RecordingStep : IEffectStep
    {
        public RecordingStep(bool onAuthority = true, bool onPrediction = false)
        {
            RunsOnAuthority = onAuthority;
            RunsOnPrediction = onPrediction;
        }

        public bool RunsOnAuthority { get; }

        public bool RunsOnPrediction { get; }

        public bool NeedsTargets { get; set; }

        public bool ProvidesTargets { get; set; }

        public int Runs { get; private set; }

        public void Execute(AbilityContext context) => Runs++;
    }

    /// <summary>
    /// Writes the private serialized fields of an authored asset, the way the inspector would.
    /// </summary>
    /// <remarks>
    /// Effect steps keep their configuration private because nothing but the inspector should write it,
    /// and adding setters so tests can reach them would be adding API to production code for the benefit
    /// of tests. Reflection over the serialised field name is the smaller cost, and it fails loudly — a
    /// renamed field throws here rather than quietly testing a default.
    /// </remarks>
    public static class AuthoredFields
    {
        public static T With<T>(this T target, string field, object value) where T : class
        {
            // Up the hierarchy by hand: a private field declared on a base class is invisible to
            // GetField on the derived type, and the id every step carries lives on ContentDefinition.
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                var info = type.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);

                if (info == null)
                {
                    continue;
                }

                info.SetValue(target, value);

                return target;
            }

            throw new MissingFieldException(target.GetType().Name, field);
        }

        /// <summary>An authored step with its id set, so validation and tracing can name it.</summary>
        public static T Step<T>(string id) where T : ScriptableObject
        {
            var step = ScriptableObject.CreateInstance<T>();

            step.With("id", id);

            return step;
        }
    }

    /// <summary>The tuning the ability tests run against, so none of them depend on the shipped file.</summary>
    public static class TestTuning
    {
        public static AbilitySettings Abilities => AbilitySettings.Default;

        public static TuningConfig Empty => new TuningConfig(
            new DictionaryTuningSource("test", new Dictionary<string, string>()));
    }
}
