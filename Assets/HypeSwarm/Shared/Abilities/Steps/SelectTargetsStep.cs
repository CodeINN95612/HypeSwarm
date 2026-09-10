using UnityEngine;

namespace HypeSwarm.Shared.Abilities.Steps
{
    /// <summary>Where the shape an ability fills is centred.</summary>
    public enum ShapeOrigin
    {
        /// <summary>On the caster. Auras, self-centred blasts, a dash that damages where it lands.</summary>
        Caster = 0,

        /// <summary>On the aim point. Placed zones and anything thrown.</summary>
        AimPoint = 1
    }

    /// <summary>
    /// Fills the target list. The step that goes in front of anything that hits things (§8.2).
    /// </summary>
    /// <remarks>
    /// Targeting is a step rather than a property of the ability, which is what lets one cast select
    /// twice — a cone of enemies to damage and then a circle of allies to shield — and what lets the
    /// second selection happen after the first one has already had an effect.
    ///
    /// <para>The shape is authored and the count is derived (§5.5.5, §5.6.5). That split is the whole
    /// design: radius and angle never scale, because area scales quadratically in effect and a linear
    /// area stat would quietly multiply damage; how many of the things inside it are hit is a discrete
    /// quantity and therefore a step function with a visible breakpoint.</para>
    /// </remarks>
    [CreateAssetMenu(menuName = "Hype Swarm/Effect Steps/Select Targets", fileName = "SelectTargets")]
    public sealed class SelectTargetsStep : EffectStep
    {
        [SerializeField]
        [Tooltip("Who to look for, relative to whoever cast this. Relative so an elite can cast the " +
                 "same ability at the party.")]
        TargetFaction wanted = TargetFaction.Enemies;

        [SerializeField]
        [Tooltip("Where the shape is centred.")]
        ShapeOrigin origin = ShapeOrigin.AimPoint;

        [SerializeField]
        [Tooltip("Radius in metres. Authored — there is no area stat.")]
        [Min(0f)]
        float radius = 4f;

        [SerializeField]
        [Tooltip("Opening angle of the cone, in degrees. 360 is a full circle.")]
        [Range(0f, 360f)]
        float coneDegrees = 360f;

        [SerializeField]
        [Tooltip("How many to hit, and what makes that number go up. A total of zero means everything " +
                 "inside the shape; anything else is nearest-first.\n\n" +
                 "This is the step-function case: a count cannot be fractional, so it moves in whole " +
                 "units at visible thresholds.")]
        StatStep maxTargets = new StatStep(0);

        [SerializeField]
        [Tooltip("Clear whatever an earlier step selected. Off to add to it instead.")]
        bool replacePrevious = true;

        /// <summary>
        /// Runs on the casting client too. The client needs to know what it thinks it hit in order to
        /// show a hit reaction immediately, even though only the host decides what actually takes damage.
        /// </summary>
        public override bool RunsOnPrediction => true;

        public override bool ProvidesTargets => true;

        /// <summary>The cap and its next breakpoint, for the HUD (§5.6.5).</summary>
        public StatStep MaxTargets => maxTargets;

        /// <summary>
        /// Who this looks for. Read-only exposure of authored data, for presentation: the shape a cast
        /// draws on the ground is this step's shape, so the drawing is always the real hit area.
        /// </summary>
        public TargetFaction Wanted => wanted;

        public ShapeOrigin Origin => origin;

        public float Radius => radius;

        public float ConeDegrees => coneDegrees;

        public override void Execute(AbilityContext context)
        {
            if (context?.World == null || context.Caster == null)
            {
                return;
            }

            if (replacePrevious)
            {
                context.Targets.Clear();
            }

            var centre = origin == ShapeOrigin.AimPoint ? context.Aim.Point : context.Caster.Position;
            var direction = context.Aim.HasDirection ? context.Aim.Direction : Vector2.zero;

            var query = new TargetQuery(
                centre,
                radius,
                context.Caster.Faction,
                wanted,
                direction,
                coneDegrees,
                context.Caster,
                maxTargets.Count(context.CasterStats));

            context.World.FindTargets(query, context.Targets);
        }
    }
}
