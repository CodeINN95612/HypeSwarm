using HypeSwarm.Shared.Abilities;
using HypeSwarm.Shared.Stats;
using NUnit.Framework;

namespace HypeSwarm.Shared.Tests
{
    /// <summary>
    /// The step function: a discrete count derived from a continuous stat, and the breakpoint the UI has
    /// to show (§5.6.5).
    /// </summary>
    /// <remarks>
    /// The spec is explicit that this needs solving at the same time as the mechanic, because a player
    /// who cannot see the next threshold cannot tell whether the next purchase does anything. So the
    /// threshold is tested as hard as the count is.
    /// </remarks>
    [TestFixture]
    public sealed class StatStepTests
    {
        static readonly ModifierSource Item = ModifierSource.Parse("item.boots");

        static StatSheet Sheet(float moveSpeed)
        {
            var sheet = new StatSheet();

            if (moveSpeed != 0f)
            {
                sheet.Add(StatModifier.Flat(StatId.MoveSpeed, moveSpeed, Item));
            }

            return sheet;
        }

        static StatStep PerForty(int baseCount = 1, int maxCount = 0)
        {
            return new StatStep(baseCount, StatId.MoveSpeed, 40f, maxCount);
        }

        [Test]
        public void WithNoneOfTheStat_TheCountIsTheBase()
        {
            Assert.That(PerForty(2).Count(Sheet(0f)), Is.EqualTo(2));
        }

        /// <summary>
        /// The whole point: the count moves in whole units at thresholds, and 2.7 projectiles does not
        /// exist.
        /// </summary>
        [Test]
        public void TheCountGoesUpOnlyAtAThreshold()
        {
            var step = PerForty();

            Assert.That(step.Count(Sheet(39f)), Is.EqualTo(1));
            Assert.That(step.Count(Sheet(40f)), Is.EqualTo(2));
            Assert.That(step.Count(Sheet(79f)), Is.EqualTo(2));
            Assert.That(step.Count(Sheet(80f)), Is.EqualTo(3));
        }

        [Test]
        public void TheNextThresholdIsTheStatValueThatBuysOneMore()
        {
            var step = PerForty();

            Assert.That(step.NextThreshold(Sheet(0f)), Is.EqualTo(40f));
            Assert.That(step.NextThreshold(Sheet(39f)), Is.EqualTo(40f));
            Assert.That(step.NextThreshold(Sheet(40f)), Is.EqualTo(80f));
        }

        [Test]
        public void WhatIsLeftToTheNextThreshold_IsWhatTheHudShows()
        {
            Assert.That(PerForty().RemainingToNext(Sheet(30f)), Is.EqualTo(10f).Within(0.001f));
        }

        [Test]
        public void ACapStopsTheCountAndSaysSo()
        {
            var step = PerForty(1, maxCount: 3);

            Assert.That(step.Count(Sheet(400f)), Is.EqualTo(3));
            Assert.That(step.AtCap(Sheet(400f)), Is.True);
            Assert.That(step.NextThreshold(Sheet(400f)), Is.EqualTo(float.PositiveInfinity),
                "at the cap there is no next threshold, and the HUD has to be able to say so");
        }

        [Test]
        public void WithoutAPerValue_TheStatAddsNothing()
        {
            var step = new StatStep(2, StatId.MoveSpeed, 0f);

            Assert.That(step.Scales, Is.False);
            Assert.That(step.Count(Sheet(1000f)), Is.EqualTo(2));
            Assert.That(step.NextThreshold(Sheet(1000f)), Is.EqualTo(float.PositiveInfinity));
        }

        [Test]
        public void ANegativeStat_NeverReducesTheCountBelowTheBase()
        {
            Assert.That(PerForty(2).Count(Sheet(-500f)), Is.EqualTo(2));
        }

        [Test]
        public void WithNoSheetAtAll_TheCountIsTheBase()
        {
            Assert.That(PerForty(3).Count(null), Is.EqualTo(3));
        }

        /// <summary>
        /// Reading the derived effect rather than the stored value bounds the count with the stat, which
        /// is what an unbounded count would not be.
        /// </summary>
        [Test]
        public void ReadingTheDerivedEffect_BoundsTheCountWithTheStat()
        {
            // Move speed derives asymptotically, so the effect is under 1 however much is stacked; one
            // more per 0.25 of it is therefore at most four more, at any input.
            var step = new StatStep(1, StatId.MoveSpeed, 0.25f, 0, StatRead.Effect);

            Assert.That(step.Count(Sheet(1_000_000f)), Is.LessThanOrEqualTo(5));
            Assert.That(step.Count(Sheet(0f)), Is.EqualTo(1));
        }

        /// <summary>
        /// Bonus reads only what the build added, so a champion's own starting value does not pay for it.
        /// </summary>
        [Test]
        public void ReadingTheBonus_IgnoresTheBaseValue()
        {
            var sheet = new StatSheet();

            sheet.SetBase(StatId.MoveSpeed, 100f);

            var step = new StatStep(0, StatId.MoveSpeed, 40f, 0, StatRead.Bonus);

            Assert.That(step.Count(sheet), Is.Zero, "the base value must not buy any");

            sheet.Add(StatModifier.Flat(StatId.MoveSpeed, 80f, Item));

            Assert.That(step.Count(sheet), Is.EqualTo(2));
        }

        [Test]
        public void AnAbsurdStatValue_DoesNotProduceAnAbsurdCount()
        {
            var step = PerForty(1, maxCount: 6);

            Assert.That(step.Count(Sheet(float.MaxValue)), Is.EqualTo(6));
        }
    }
}
