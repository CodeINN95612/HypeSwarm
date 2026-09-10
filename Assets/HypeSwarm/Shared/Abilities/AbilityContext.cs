using System.Collections.Generic;
using HypeSwarm.Shared.Combat;
using HypeSwarm.Shared.Stats;

namespace HypeSwarm.Shared.Abilities
{
    /// <summary>
    /// One cast, in flight: who cast it, where, what it has found, and what has been done to it so far
    /// (§8.1).
    /// </summary>
    /// <remarks>
    /// <b>Mutable, and created fresh for every cast.</b> That is the point — effect steps hand each
    /// other work through it, and in Phase 3 augments will mutate it on the way past. A reused context
    /// would leak one cast into the next, which is the kind of bug that shows up as an ability
    /// occasionally hitting something it was not aimed at.
    ///
    /// <para>One allocation per cast is affordable at five players and will not be at horde density
    /// once elites cast (§6.4). The fix then is a pool, and the reason it can be a pool later is that
    /// nothing holds a reference to a context after the cast finishes. Nothing may start.</para>
    /// </remarks>
    public sealed class AbilityContext
    {
        /// <summary>
        /// How deep the trigger chain may go before it is refused.
        /// </summary>
        /// <remarks>
        /// The guard itself lands in Phase 3 step 9 with the hook pipeline, because there is nothing to
        /// loop yet. The counter is here now because it has to be threaded through every cast, and
        /// adding a field to the context later means revisiting every site that makes one.
        /// </remarks>
        public const int MaxDepth = 8;

        /// <summary>What is casting. Never null.</summary>
        public ICombatant Caster { get; set; }

        /// <summary>The ability being cast.</summary>
        public IAbilityDefinition Ability { get; set; }

        /// <summary>Which indexed slot it came from (§8.3).</summary>
        public int Slot { get; set; }

        /// <summary>
        /// Who to credit for what this does — the ability, and which instance of it. What shields and
        /// timed modifiers are applied under, so they can be removed again as a unit (§9).
        /// </summary>
        public ModifierSource Source { get; set; }

        /// <summary>Where it was aimed, already clamped to range.</summary>
        public AbilityAim Aim { get; set; }

        /// <summary>
        /// True on the machine whose decisions count. False on a casting client running the same steps
        /// for its own presentation (§5.5.1).
        /// </summary>
        public bool IsAuthority { get; set; }

        /// <summary>Where to look for targets. Never null in a real cast.</summary>
        public IAbilityWorld World { get; set; }

        /// <summary>
        /// The caster's motor, when this machine owns it. Null otherwise, and a dash step does nothing
        /// rather than moving somebody else's champion.
        /// </summary>
        public IMobility Mobility { get; set; }

        /// <summary>
        /// Seconds a channel was held, for effects that scale with commitment (§5.5.4). Zero for
        /// everything else.
        /// </summary>
        public float ChannelDuration { get; set; }

        /// <summary>
        /// Seconds this execution stands for, for anything authored as a rate. Zero for a single cast;
        /// the pulse interval for a passive, so a passive authored as damage per second keeps dealing
        /// damage per second when the tick rate is retuned for performance (§5.5.3).
        /// </summary>
        public float DeltaTime { get; set; }

        /// <summary>How many triggers deep this is. See <see cref="MaxDepth"/>.</summary>
        public int Depth { get; set; }

        /// <summary>
        /// What this cast has found. Filled by a targeting step, read by everything after it, and the
        /// reason an ability is an ordered list rather than a set.
        /// </summary>
        public List<ICombatant> Targets { get; } = new List<ICombatant>();

        /// <summary>
        /// Tags that apply to this cast in particular, on top of the ability's own.
        /// </summary>
        /// <remarks>
        /// Strings and a plain set, deliberately temporary: Phase 3 step 9 formalises tags across
        /// champions, abilities, items and augments, and whatever type comes out of that replaces this.
        /// It is here now because §8.1 puts the mutable tag set on the context, and because an augment
        /// marking a cast ("this one is energized") has nowhere else to write.
        /// </remarks>
        public HashSet<string> Tags { get; } = new HashSet<string>();

        /// <summary>The caster's stats, or null. What every scaling coefficient reads (§5.6.5).</summary>
        public StatSheet CasterStats => Caster?.Stats;

        /// <summary>Whether anything was found to hit. Worth checking before doing work per target.</summary>
        public bool HasTargets => Targets.Count > 0;

        /// <summary>
        /// The damage packet this cast would send for an amount, already carrying its provenance.
        /// </summary>
        /// <remarks>
        /// Built here rather than in each damaging step so that every packet one cast produces agrees
        /// about who sent it and what it came from. Provenance is what the Source axis is for (§5.6.1),
        /// and a packet that does not record it cannot be retrofitted without revisiting every step
        /// that ever made one.
        /// </remarks>
        public DamagePacket Packet(float amount, DamageFlags flags)
        {
            return new DamagePacket(amount, Source, flags, Caster == null ? 0u : Caster.Id);
        }
    }
}
