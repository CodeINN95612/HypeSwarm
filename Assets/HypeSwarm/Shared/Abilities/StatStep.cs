using System;
using HypeSwarm.Shared.Stats;
using UnityEngine;

namespace HypeSwarm.Shared.Abilities
{
    /// <summary>
    /// A discrete quantity derived from a continuous stat: a count of projectiles, pulses, or targets.
    /// The step function §5.6.5 insists on.
    /// </summary>
    /// <remarks>
    /// <b>"Projectile count scales with move speed" cannot mean 2.7 projectiles.</b> So it is authored
    /// as a step function — one more per <see cref="Per"/> of the stat — and the threshold is exposed
    /// rather than left implicit, because a player who cannot see the next breakpoint cannot tell
    /// whether the next purchase does anything at all. A continuous stat silently rounding down is
    /// one of the meanest ways for an item to be worthless.
    ///
    /// <para><see cref="NextThreshold"/> is therefore part of the type rather than something the HUD
    /// recomputes for itself: the spec asks for the mechanic and its readout to be solved together,
    /// and two implementations of the same arithmetic eventually disagree.</para>
    ///
    /// <para>Pure and sheet-only — no clock, no entity — so the thresholds are tested directly.</para>
    /// </remarks>
    [Serializable]
    public struct StatStep
    {
        [SerializeField]
        [Tooltip("How many there are with none of the stat at all.")]
        int baseCount;

        [SerializeField]
        [Tooltip("The stat that adds more.")]
        StatId stat;

        [SerializeField]
        [Tooltip("Which number to read: the total, only what the build added, or the derived effect.")]
        StatRead read;

        [SerializeField]
        [Tooltip("Stat per extra one. Zero means the stat does not add any.")]
        float per;

        [SerializeField]
        [Tooltip("Ceiling on the total. Zero means unbounded, which at horde density is a performance " +
                 "problem rather than a generous design, so bound it.")]
        int maxCount;

        public StatStep(int baseCount, StatId stat = StatId.MoveSpeed, float per = 0f, int maxCount = 0, StatRead read = StatRead.Total)
        {
            this.baseCount = baseCount;
            this.stat = stat;
            this.read = read;
            this.per = per;
            this.maxCount = maxCount;
        }

        public int BaseCount => baseCount;

        public StatId Stat => stat;

        public StatRead Read => read;

        /// <summary>Stat value that buys one more. Zero disables scaling entirely.</summary>
        public float Per => per;

        /// <summary>Ceiling on the count, or zero for none.</summary>
        public int MaxCount => maxCount;

        /// <summary>Whether the stat adds anything at all. For tooltips and for validation.</summary>
        public bool Scales => per > 0f;

        /// <summary>
        /// How many there are for this sheet. Never fractional, never below <see cref="BaseCount"/>,
        /// never above <see cref="MaxCount"/> when one is set.
        /// </summary>
        public int Count(StatSheet sheet)
        {
            var total = baseCount + Extra(sheet);

            if (total < 0)
            {
                total = 0;
            }

            return maxCount > 0 && total > maxCount ? maxCount : total;
        }

        /// <summary>
        /// The stat value at which the count goes up next, or <see cref="float.PositiveInfinity"/>
        /// once the cap is reached. <b>The number the HUD shows.</b>
        /// </summary>
        public float NextThreshold(StatSheet sheet)
        {
            if (!Scales || AtCap(sheet))
            {
                return float.PositiveInfinity;
            }

            return (Extra(sheet) + 1) * per;
        }

        /// <summary>How much more of the stat is needed for one more.</summary>
        public float RemainingToNext(StatSheet sheet)
        {
            var threshold = NextThreshold(sheet);

            if (float.IsPositiveInfinity(threshold))
            {
                return float.PositiveInfinity;
            }

            var remaining = threshold - Value(sheet);

            return remaining < 0f ? 0f : remaining;
        }

        /// <summary>True once more of the stat buys nothing, which the HUD should say out loud.</summary>
        public bool AtCap(StatSheet sheet) => maxCount > 0 && Count(sheet) >= maxCount;

        int Extra(StatSheet sheet)
        {
            if (!Scales)
            {
                return 0;
            }

            var value = Value(sheet);

            if (value <= 0f || float.IsNaN(value))
            {
                return 0;
            }

            // In double, and bounded before the cast. A float stat can be large enough that the integer
            // conversion overflows, and an overflowed count reads as a negative one — which would turn
            // an absurd amount of a stat into no projectiles at all rather than the maximum.
            var steps = Math.Floor((double)value / per);

            if (steps >= int.MaxValue)
            {
                return maxCount > 0 ? maxCount : int.MaxValue - baseCount;
            }

            return (int)steps;
        }

        float Value(StatSheet sheet)
        {
            if (sheet == null)
            {
                return 0f;
            }

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
            if (!Scales)
            {
                return baseCount.ToString();
            }

            var text = baseCount + " + 1 per " + per.ToString("0.##") + " " + stat;

            return maxCount > 0 ? text + " (max " + maxCount + ")" : text;
        }
    }
}
