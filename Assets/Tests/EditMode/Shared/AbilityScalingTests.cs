using HypeSwarm.Shared.Abilities;
using HypeSwarm.Shared.Stats;
using NUnit.Framework;

namespace HypeSwarm.Shared.Tests
{
    /// <summary>
    /// The one bridge between an ability and a stat: a flat amount plus a coefficient (§5.6.5).
    /// </summary>
    /// <remarks>
    /// Small enough to look not worth testing, and it is the arithmetic every number in the game
    /// eventually passes through. The three read modes are genuinely different designs and confusing
    /// them is a silent balance bug: total and bonus differ by exactly the champion's own base, which is
    /// most of the number early and almost none of it late.
    /// </remarks>
    [TestFixture]
    public sealed class AbilityScalingTests
    {
        static readonly ModifierSource Item = ModifierSource.Parse("item.sword");

        static StatSheet Sheet(StatId stat, float baseValue, float added)
        {
            var sheet = new StatSheet();

            sheet.SetBase(stat, baseValue);

            if (added != 0f)
            {
                sheet.Add(StatModifier.Flat(stat, added, Item));
            }

            return sheet;
        }

        [Test]
        public void AFlatAmountWithNoCoefficient_IsTheFlatAmount()
        {
            var scaling = new AbilityScaling(25f);

            Assert.That(scaling.Resolve(Sheet(StatId.Damage, 100f, 500f)), Is.EqualTo(25f));
        }

        [Test]
        public void ACoefficientMultipliesTheStat()
        {
            var scaling = new AbilityScaling(10f, StatId.Damage, 1.5f);

            Assert.That(scaling.Resolve(Sheet(StatId.Damage, 0f, 40f)), Is.EqualTo(70f));
        }

        /// <summary>
        /// The §5.6.5 example: an ability that scales with <i>bonus</i> max HP is worth nothing at minute
        /// zero and scales with the build rather than with the champion.
        /// </summary>
        [Test]
        public void ReadingTheBonus_IgnoresWhatTheChampionStartedWith()
        {
            var scaling = new AbilityScaling(0f, StatId.MaxHealth, 0.1f, StatRead.Bonus);
            var sheet = Sheet(StatId.MaxHealth, 600f, 0f);

            Assert.That(scaling.Resolve(sheet), Is.Zero);

            sheet.Add(StatModifier.Flat(StatId.MaxHealth, 300f, Item));

            Assert.That(scaling.Resolve(sheet), Is.EqualTo(30f).Within(0.001f));
        }

        /// <summary>
        /// Reading the derived effect saturates with the stat instead of growing with it, which is what
        /// anything that must stay bounded wants.
        /// </summary>
        [Test]
        public void ReadingTheEffect_IsBoundedHoweverMuchOfTheStatThereIs()
        {
            var scaling = new AbilityScaling(0f, StatId.DamageReduction, 100f, StatRead.Effect);

            Assert.That(scaling.Resolve(Sheet(StatId.DamageReduction, 0f, 1e12f)), Is.LessThan(100f));
            Assert.That(scaling.Resolve(Sheet(StatId.DamageReduction, 0f, 1e12f)), Is.GreaterThan(99f));
        }

        /// <summary>
        /// A destructible or a trash mob has no sheet. The flat part is the honest answer — the
        /// alternative is a division by a null reference on every cast at horde density.
        /// </summary>
        [Test]
        public void WithNoSheet_OnlyTheFlatPartLands()
        {
            var scaling = new AbilityScaling(12f, StatId.Damage, 10f);

            Assert.That(scaling.Resolve(null), Is.EqualTo(12f));
            Assert.That(scaling.ScaledPart(null), Is.Zero);
        }

        [Test]
        public void TheScaledPartIsReportedSeparately_ForATooltipThatExplainsItself()
        {
            var scaling = new AbilityScaling(10f, StatId.Damage, 2f);
            var sheet = Sheet(StatId.Damage, 0f, 30f);

            Assert.That(scaling.ScaledPart(sheet), Is.EqualTo(60f));
            Assert.That(scaling.Resolve(sheet), Is.EqualTo(70f));
        }

        /// <summary>
        /// A shred pushes damage reduction negative, and an ability scaling from a negative stat should
        /// come out lower rather than throwing or clamping somewhere invisible.
        /// </summary>
        [Test]
        public void ANegativeStat_ScalesDownwards()
        {
            var scaling = new AbilityScaling(100f, StatId.DamageReduction, 1f);

            Assert.That(scaling.Resolve(Sheet(StatId.DamageReduction, 0f, -40f)), Is.EqualTo(60f));
        }
    }
}
