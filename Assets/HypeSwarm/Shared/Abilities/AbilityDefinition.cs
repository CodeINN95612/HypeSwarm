using System;
using System.Collections.Generic;
using HypeSwarm.Shared.Abilities.Steps;
using HypeSwarm.Shared.Content;
using UnityEngine;

namespace HypeSwarm.Shared.Abilities
{
    /// <summary>
    /// One authored ability. Immutable data and an ordered list of effect steps (§8.1).
    /// </summary>
    /// <remarks>
    /// <b>The exit criterion for this phase step is that an ability is authored entirely in here.</b>
    /// Nothing on this asset is a subclass hook and there is no place to put code: an ability is a cast
    /// cost, a cooldown, a targeting mode and a list of steps, so a new ability is a new asset rather
    /// than a new script. When that stops being true — when an ability needs a step that does not exist
    /// — the answer is a new step type that every other ability can then use, not a special case here.
    ///
    /// <para><b>No runtime state, ever.</b> This is one shared asset; two players on the same champion
    /// would share any field that changed. Cooldowns, charges and stacks live on
    /// <see cref="AbilityInstance"/>. That is on the spec's trap list and it is the single easiest
    /// mistake to make in this file.</para>
    /// </remarks>
    [CreateAssetMenu(menuName = "Hype Swarm/Ability", fileName = "Ability")]
    public sealed class AbilityDefinition : ContentDefinition, IAbilityDefinition
    {
        [Header("Identity")]
        [SerializeField]
        [Tooltip("What the player is shown. The id is what the game uses.")]
        string displayName;

        [SerializeField]
        [Tooltip("Which of the five slots this belongs in. Every champion has exactly one of each.")]
        AbilityRole role = AbilityRole.Primary;

        [Header("Cast")]
        [SerializeField]
        [Tooltip("What using this costs in movement.\n\n" +
                 "Free casts while moving, Slowed keeps part of your speed, Rooted stops you, and " +
                 "Channelled stops you while it builds. This axis is how kiting works, so an ability " +
                 "that is free to cast should be free for a reason.")]
        CastCost castCost = CastCost.Free;

        [SerializeField]
        [Tooltip("Seconds between the input and the effect. Zero resolves on the same frame.")]
        [Min(0f)]
        float castTime;

        [SerializeField]
        [Tooltip("For a channel: the longest it may be held before it resolves by itself.")]
        [Min(0f)]
        float maxChannelDuration;

        [Header("Cooldown")]
        [SerializeField]
        [Tooltip("Seconds before it can be used again, before haste.")]
        [Min(0f)]
        float cooldown = 1f;

        [SerializeField]
        [Tooltip("How many uses are banked. More than one is the mobility-as-a-resource model.")]
        [Min(1)]
        int charges = 1;

        [SerializeField]
        [Tooltip("Seconds that must pass between two uses even when charges are available. Keeps one " +
                 "frame of input from spending two charges.")]
        [Min(0f)]
        float chargeLockout;

        [Header("Aiming")]
        [SerializeField]
        [Tooltip("How the cast is aimed. Nothing in this game is click-targeted.")]
        TargetingMode targeting = TargetingMode.AimPoint;

        [SerializeField]
        [Tooltip("How far from the caster the aim point may be, in metres. Authored per ability and " +
                 "never scaled by a stat — there is no range stat and there is not going to be one.")]
        [Min(0f)]
        float range = 8f;

        [Header("Effect")]
        [SerializeField]
        [Tooltip("What happens the moment the cast starts, before the cast time or the channel.\n\n" +
                 "Almost always empty. For the damage reduction a channel grants while it is held, or " +
                 "a telegraph a wind-up shows.")]
        EffectStep[] startSteps = Array.Empty<EffectStep>();

        [SerializeField]
        [Tooltip("What it does when it resolves, in order. A targeting step comes before anything that " +
                 "hits what it found.")]
        EffectStep[] steps = Array.Empty<EffectStep>();

        // Memoised widening of the authored arrays. A pure function of authored data, identical for
        // every reader and discarded on reload — not the runtime state the class docs forbid.
        [NonSerialized] IEffectStep[] widened;
        [NonSerialized] IEffectStep[] widenedStart;

        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;

        public AbilityRole Role => role;

        public CastCost CastCost => castCost;

        public float CastTime => castTime;

        public float MaxChannelDuration => maxChannelDuration;

        public float Cooldown => cooldown;

        public int Charges => charges;

        public float ChargeLockout => chargeLockout;

        public TargetingMode Targeting => targeting;

        public float Range => range;

        public IReadOnlyList<IEffectStep> StartSteps => widenedStart ??= Widen(startSteps);

        public IReadOnlyList<IEffectStep> Steps => widened ??= Widen(steps);

        protected override void OnValidate()
        {
            base.OnValidate();

            widened = null;
            widenedStart = null;
        }

        static IEffectStep[] Widen(EffectStep[] authored)
        {
            if (authored == null)
            {
                return Array.Empty<IEffectStep>();
            }

            var result = new IEffectStep[authored.Length];

            for (var i = 0; i < authored.Length; i++)
            {
                result[i] = authored[i];
            }

            return result;
        }
    }
}
