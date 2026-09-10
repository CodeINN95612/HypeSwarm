using HypeSwarm.Shared.Combat;
using HypeSwarm.Shared.Movement;
using UnityEngine;

namespace HypeSwarm.Shared.Abilities
{
    /// <summary>
    /// A shape, a side, and a limit: everything needed to decide whether one combatant is hit.
    /// </summary>
    /// <remarks>
    /// <b>The filtering lives here rather than in the world</b>, which is what keeps it testable. An
    /// <see cref="IAbilityWorld"/> is responsible only for producing candidates efficiently — a list
    /// scan today, a spatial hash at Phase 2 step 7 — and both must agree on what counts as a hit, so
    /// neither of them decides.
    ///
    /// <para>Shapes are planar. The game is 3D but combat is not: enemies are on the floor, abilities
    /// are aimed across it, and measuring in three dimensions would make a cone miss something
    /// standing on a step. Height is deliberately ignored (§5.5.1).</para>
    ///
    /// <para>Ranges and radii are authored, never scaled by a stat (§5.5.5). There is no area stat and
    /// there will not be one — area scales quadratically in effect, so a linear area stat quietly
    /// multiplies damage.</para>
    /// </remarks>
    public readonly struct TargetQuery
    {
        /// <summary>A cone angle of this or more is a full circle, and the angle test is skipped.</summary>
        public const float FullCircleDegrees = 360f;

        public TargetQuery(
            Vector3 origin,
            float radius,
            Faction casterFaction,
            TargetFaction wanted = TargetFaction.Enemies,
            Vector2 direction = default,
            float coneDegrees = FullCircleDegrees,
            ICombatant caster = null,
            int maxTargets = 0)
        {
            Origin = origin;
            Radius = radius;
            CasterFaction = casterFaction;
            Wanted = wanted;
            Direction = direction;
            ConeDegrees = coneDegrees;
            Caster = caster;
            MaxTargets = maxTargets;
        }

        /// <summary>Centre of the shape — the caster, or the aim point.</summary>
        public Vector3 Origin { get; }

        /// <summary>Planar radius in metres.</summary>
        public float Radius { get; }

        /// <summary>Which way a cone opens. Ignored for a full circle.</summary>
        public Vector2 Direction { get; }

        /// <summary>Total opening angle of the cone, not the half-angle. 360 for a circle.</summary>
        public float ConeDegrees { get; }

        public Faction CasterFaction { get; }

        public TargetFaction Wanted { get; }

        /// <summary>
        /// Who is casting. Needed so the caster can be excluded from its own enemy search and
        /// included in its own ally search.
        /// </summary>
        public ICombatant Caster { get; }

        /// <summary>
        /// Most targets to return, nearest first, or zero for every match.
        /// </summary>
        /// <remarks>
        /// Nearest first is not arbitrary. A capped ability that picked whichever enemies the
        /// container happened to list first would hit different things on two machines and would feel
        /// random to the player, which for an aimed ability is the one thing it must not feel.
        /// </remarks>
        public int MaxTargets { get; }

        /// <summary>Whether this query wants everything in range rather than a limited number.</summary>
        public bool IsUnlimited => MaxTargets <= 0;

        /// <summary>The same query with a different cap, for a count that came from a step function.</summary>
        public TargetQuery WithMaxTargets(int maxTargets)
        {
            return new TargetQuery(
                Origin, Radius, CasterFaction, Wanted, Direction, ConeDegrees, Caster, maxTargets);
        }

        /// <summary>
        /// Whether this combatant is hit. The whole definition of a hit, in one pure function.
        /// </summary>
        public bool Matches(ICombatant candidate)
        {
            if (candidate == null || candidate.IsDead)
            {
                return false;
            }

            if (!WantsFaction(candidate))
            {
                return false;
            }

            if (Radius <= 0f)
            {
                // A shapeless query still resolves for the caster, which is how a self-shield works
                // without authoring a radius nobody reads.
                return Wanted == TargetFaction.Caster;
            }

            var offset = MotionPlane.Flatten(candidate.Position - Origin);

            if (offset.sqrMagnitude > Radius * Radius)
            {
                return false;
            }

            return WithinCone(offset);
        }

        /// <summary>Planar distance from the centre of the shape. For sorting nearest-first.</summary>
        public float DistanceTo(ICombatant candidate)
        {
            return candidate == null
                ? float.PositiveInfinity
                : MotionPlane.Flatten(candidate.Position - Origin).magnitude;
        }

        bool WantsFaction(ICombatant candidate)
        {
            switch (Wanted)
            {
                case TargetFaction.Caster:
                    return Caster != null && ReferenceEquals(candidate, Caster);

                case TargetFaction.Allies:
                    return Factions.AreAllied(CasterFaction, candidate.Faction);

                default:
                    return Factions.AreHostile(CasterFaction, candidate.Faction);
            }
        }

        bool WithinCone(Vector2 offset)
        {
            if (ConeDegrees >= FullCircleDegrees || ConeDegrees <= 0f)
            {
                return true;
            }

            if (Direction.sqrMagnitude <= 0f)
            {
                // A cone with no direction is an authoring mistake rather than a cone that hits
                // nothing. Reporting it belongs to validation; behaving like a circle is the kinder
                // failure at runtime.
                return true;
            }

            if (offset.sqrMagnitude <= 0f)
            {
                // Standing inside the caster. Every cone hits what is on top of it.
                return true;
            }

            return Vector2.Angle(Direction, offset) <= ConeDegrees * 0.5f;
        }
    }
}
