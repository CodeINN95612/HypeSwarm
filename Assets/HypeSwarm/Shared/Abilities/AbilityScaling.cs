using System;
using HypeSwarm.Shared.Stats;
using UnityEngine;

namespace HypeSwarm.Shared.Abilities
{
    /// <summary>
    /// A number an ability authors for itself: a flat amount plus a coefficient against one stat.
    /// </summary>
    /// <remarks>
    /// <b>This is the whole of the one-way dependency</b> (§5.6.5). The ability reads the stat sheet;
    /// the stat sheet has never heard of the ability. Every consequence the spec claims for that
    /// falls out of this struct being the only bridge:
    ///
    /// <list type="bullet">
    /// <item>Every stat is relevant to someone — damage reduction is offensive on the champion whose
    /// shield scales from it, move speed is damage on the passive that reads it.</item>
    /// <item>Balance stays local. Changing a coefficient touches one ability asset.</item>
    /// <item>Stats stay a flat cacheable numeric layer with no special cases.</item>
    /// </list>
    ///
    /// <para>One stat per number, deliberately. Two would invite three, and an ability whose damage
    /// reads four stats is an ability nobody can reason about or fit in a tooltip. An ability that
    /// genuinely needs two contributions authors two steps.</para>
    /// </remarks>
    [Serializable]
    public struct AbilityScaling
    {
        [SerializeField]
        [Tooltip("Added regardless of stats. The floor this has at minute zero.")]
        float flat;

        [SerializeField]
        [Tooltip("The stat this scales from.")]
        StatId stat;

        [SerializeField]
        [Tooltip("Which number to read off it: the total, only what the build added, or the derived bounded effect.")]
        StatRead read;

        [SerializeField]
        [Tooltip("Multiplier on the stat. 1 means one point of the stat is one point of this.")]
        float coefficient;

        public AbilityScaling(float flat, StatId stat = StatId.Damage, float coefficient = 0f, StatRead read = StatRead.Total)
        {
            this.flat = flat;
            this.stat = stat;
            this.read = read;
            this.coefficient = coefficient;
        }

        public float Flat => flat;

        public StatId Stat => stat;

        public StatRead Read => read;

        public float Coefficient => coefficient;

        /// <summary>
        /// The number, for this sheet. Returns the flat part when there is no sheet, so an entity
        /// without stats — a destructible, a test double — still does something rather than nothing.
        /// </summary>
        public float Resolve(StatSheet sheet)
        {
            if (coefficient == 0f || sheet == null)
            {
                return flat;
            }

            return flat + coefficient * ReadFrom(sheet);
        }

        /// <summary>The stat contribution alone, for a tooltip that breaks a number down.</summary>
        public float ScaledPart(StatSheet sheet)
        {
            return sheet == null || coefficient == 0f ? 0f : coefficient * ReadFrom(sheet);
        }

        float ReadFrom(StatSheet sheet)
        {
            switch (read)
            {
                case StatRead.Bonus:
                    return sheet.Get(stat) - sheet.GetBase(stat);

                case StatRead.Effect:
                    return sheet.Effect(stat);

                default:
                    return sheet.Get(stat);
            }
        }

        public override string ToString()
        {
            return coefficient == 0f ? Format(flat) : Format(flat) + " + " + Format(coefficient) + " per " + stat;
        }

        static string Format(float value) => value.ToString("0.##");
    }
}
