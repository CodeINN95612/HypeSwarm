using HypeSwarm.Shared.Combat;
using UnityEngine;

namespace HypeSwarm.Shared.Abilities.Steps
{
    /// <summary>
    /// Damages everything the targeting step found, through the one pipeline (§5.6.5).
    /// </summary>
    /// <remarks>
    /// <b>The amount is fully resolved here, before the packet is made.</b> The pipeline reads only
    /// penetration off the attacker; every scaling coefficient is applied at this point, which is what
    /// the one-way dependency means in practice — mitigation does not know or care which ability sent
    /// the packet or what it scaled from.
    ///
    /// <para>Every point of damage in the game goes through <see cref="DamagePipeline.Apply"/>, never
    /// straight to a health pool. It is the choke point every augment will hook, and a second path is
    /// how half the augments in the game silently stop applying (§8.4).</para>
    /// </remarks>
    [CreateAssetMenu(menuName = "Hype Swarm/Effect Steps/Deal Damage", fileName = "DealDamage")]
    public sealed class DealDamageStep : EffectStep
    {
        [SerializeField]
        [Tooltip("How much, and what it scales from. The coefficient is this ability's own — changing " +
                 "it touches nothing else in the game.")]
        AbilityScaling amount = new AbilityScaling(10f, Stats.StatId.Damage, 1f);

        [SerializeField]
        [Tooltip("Where this damage came from. Provenance, not a resistance type — there are no damage " +
                 "schools, and the source axis is what augments will read instead.")]
        DamageFlags flags = DamageFlags.Ability;

        [SerializeField]
        [Tooltip("Treat the amount as damage per second rather than per hit.\n\n" +
                 "For passives and anything else that runs on a tick: the amount is multiplied by the " +
                 "seconds the tick stands for, so retuning the tick rate for performance does not " +
                 "change how much damage the passive does.")]
        bool perSecond;

        [SerializeField]
        [Tooltip("Extra damage per second of channel, as a fraction. 0.5 means a 2-second channel hits " +
                 "twice as hard. Zero for everything that is not channelled.")]
        [Min(0f)]
        float channelBonusPerSecond;

        public override bool NeedsTargets => true;

        public override void Execute(AbilityContext context)
        {
            if (context == null || !context.HasTargets)
            {
                return;
            }

            var damage = Resolve(context);

            if (damage <= 0f)
            {
                return;
            }

            var packet = context.Packet(damage, flags);

            for (var i = 0; i < context.Targets.Count; i++)
            {
                DamagePipeline.Apply(context.Targets[i], packet);
            }
        }

        /// <summary>
        /// What one target would take, before mitigation. Public for the trace tool and for tests that
        /// want the arithmetic without a world.
        /// </summary>
        public float Resolve(AbilityContext context)
        {
            var damage = amount.Resolve(context.CasterStats);

            if (perSecond)
            {
                // A tick that stands for no time deals no damage, which is the right answer for a
                // per-second step that somehow ran outside a tick rather than a full hit.
                damage *= context.DeltaTime;
            }

            if (channelBonusPerSecond > 0f && context.ChannelDuration > 0f)
            {
                damage *= 1f + channelBonusPerSecond * context.ChannelDuration;
            }

            return damage;
        }
    }
}
