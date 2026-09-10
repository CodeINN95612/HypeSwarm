using HypeSwarm.Shared.Stats;
using UnityEngine;

namespace HypeSwarm.Shared.Abilities.Steps
{
    /// <summary>
    /// Applies a timed stat modifier: a slow, a shred, a haste buff, a defensive stance.
    /// </summary>
    /// <remarks>
    /// <b>One step covers every buff and debuff in the game</b>, because a slow <i>is</i> negative move
    /// speed and a shred <i>is</i> negative damage reduction. Giving them a bespoke status system would
    /// mean two places a number can come from, which is the thing §5.6.5 exists to prevent — and the
    /// derived curves already handle the awkward cases: a slow cannot stop a champion dead, and a shred
    /// that pushes damage reduction below zero amplifies instead of inverting.
    ///
    /// <para>Timed rather than permanent, and applied under the ability's source, so it comes off as a
    /// unit and a second cast refreshes rather than stacks (§9).</para>
    ///
    /// <para><b>Slow resistance is not read here yet.</b> The stat exists and the seam for it is the
    /// motor's speed multiplier, which is where a slow and a haste already meet; wiring resistance into
    /// the magnitude belongs with status handling proper, and doing it here would put a second opinion
    /// about how strong a slow is inside an effect step.</para>
    /// </remarks>
    [CreateAssetMenu(menuName = "Hype Swarm/Effect Steps/Apply Stat Modifier", fileName = "ApplyStatModifier")]
    public sealed class ApplyStatModifierStep : EffectStep
    {
        [SerializeField]
        [Tooltip("Apply to the caster rather than the selected targets. On for a self-buff.")]
        bool onCaster;

        [SerializeField]
        [Tooltip("Which stat to move.")]
        StatId stat = StatId.MoveSpeed;

        [SerializeField]
        [Tooltip("How it combines with everything else on that stat. Flat adds, the percentages stack " +
                 "in their documented order.")]
        ModifierOperation operation = ModifierOperation.FlatAdd;

        [SerializeField]
        [Tooltip("How much, and what it scales from. Negative for a slow or a shred.")]
        AbilityScaling magnitude = new AbilityScaling(-30f);

        [SerializeField]
        [Tooltip("Seconds before it comes off.")]
        [Min(0.01f)]
        float duration = 2f;

        public override bool NeedsTargets => !onCaster;

        public override void Execute(AbilityContext context)
        {
            if (context?.Caster == null)
            {
                return;
            }

            var value = magnitude.Resolve(context.CasterStats);

            if (value == 0f)
            {
                return;
            }

            // One array per cast rather than one per target: the modifier carries its source, and the
            // source is the ability, so every target gets the same thing applied to its own sheet.
            var modifiers = new[] { new StatModifier(stat, operation, value, context.Source) };

            if (onCaster)
            {
                context.Caster.ApplyModifiers(context.Source, duration, modifiers);

                return;
            }

            for (var i = 0; i < context.Targets.Count; i++)
            {
                context.Targets[i]?.ApplyModifiers(context.Source, duration, modifiers);
            }
        }
    }
}
