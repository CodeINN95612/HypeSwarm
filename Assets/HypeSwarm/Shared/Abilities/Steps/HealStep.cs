using UnityEngine;

namespace HypeSwarm.Shared.Abilities.Steps
{
    /// <summary>Restores health, on the caster or on whatever was selected.</summary>
    /// <remarks>
    /// Health restored is not the same as a shield and the two are authored separately on purpose. A
    /// heal is worth nothing at full health and everything at low health; a shield is worth the same
    /// either way but expires. Both exist so a support kit has a choice to make (§5).
    /// </remarks>
    [CreateAssetMenu(menuName = "Hype Swarm/Effect Steps/Heal", fileName = "Heal")]
    public sealed class HealStep : EffectStep
    {
        [SerializeField]
        [Tooltip("Heal the caster rather than the selected targets.")]
        bool onCaster;

        [SerializeField]
        [Tooltip("How much, and what it scales from.")]
        AbilityScaling amount = new AbilityScaling(20f, Stats.StatId.MaxHealth, 0f);

        [SerializeField]
        [Tooltip("Treat the amount as healing per second rather than per cast. For passives, which run " +
                 "on a tick.")]
        bool perSecond;

        public override bool NeedsTargets => !onCaster;

        public override void Execute(AbilityContext context)
        {
            if (context?.Caster == null)
            {
                return;
            }

            var healing = amount.Resolve(context.CasterStats);

            if (perSecond)
            {
                healing *= context.DeltaTime;
            }

            if (healing <= 0f)
            {
                return;
            }

            if (onCaster)
            {
                context.Caster.Heal(healing);

                return;
            }

            for (var i = 0; i < context.Targets.Count; i++)
            {
                context.Targets[i]?.Heal(healing);
            }
        }
    }
}
