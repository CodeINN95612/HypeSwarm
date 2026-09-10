using UnityEngine;

namespace HypeSwarm.Shared.Abilities.Steps
{
    /// <summary>
    /// Puts absorption in front of health, on the caster or on whatever was selected.
    /// </summary>
    /// <remarks>
    /// The canonical demonstration that every stat is relevant to someone (§5.6.5): a shield authored to
    /// scale from damage reduction makes a defensive stat into this champion's offensive scaling, with
    /// no special case anywhere in the stat layer.
    ///
    /// <para>The shield is applied under the ability's own source, so a second cast refreshes it rather
    /// than stacking a second layer, and removing the ability removes the shield (§9). That is a design
    /// decision living in <c>ShieldPool</c>, not here — one source, one layer.</para>
    /// </remarks>
    [CreateAssetMenu(menuName = "Hype Swarm/Effect Steps/Apply Shield", fileName = "ApplyShield")]
    public sealed class ApplyShieldStep : EffectStep
    {
        [SerializeField]
        [Tooltip("Shield the caster rather than the selected targets. On for a self-shield, which then " +
                 "needs no targeting step in front of it.")]
        bool onCaster = true;

        [SerializeField]
        [Tooltip("How much, and what it scales from.")]
        AbilityScaling amount = new AbilityScaling(20f, Stats.StatId.DamageReduction, 1f);

        [SerializeField]
        [Tooltip("Seconds before it expires unspent.")]
        [Min(0.01f)]
        float duration = 4f;

        public override bool NeedsTargets => !onCaster;

        public override void Execute(AbilityContext context)
        {
            if (context?.Caster == null)
            {
                return;
            }

            var shield = amount.Resolve(context.CasterStats);

            if (shield <= 0f)
            {
                return;
            }

            if (onCaster)
            {
                context.Caster.AddShield(context.Source, shield, duration);

                return;
            }

            for (var i = 0; i < context.Targets.Count; i++)
            {
                context.Targets[i]?.AddShield(context.Source, shield, duration);
            }
        }
    }
}
