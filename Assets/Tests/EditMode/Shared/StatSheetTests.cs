using System.Collections.Generic;
using HypeSwarm.Shared.Stats;
using NUnit.Framework;

namespace HypeSwarm.Shared.Tests
{
    /// <summary>
    /// The operation order from §9 and the removal-by-source guarantee. Both are invariants listed in
    /// the working rules, and both are the kind of thing that stays wrong for a year because the
    /// numbers still look plausible.
    /// </summary>
    [TestFixture]
    public sealed class StatSheetTests
    {
        static readonly ModifierSource Ring = ModifierSource.Parse("item.iron_ring");
        static readonly ModifierSource SecondRing = ModifierSource.Parse("item.iron_ring", 1);
        static readonly ModifierSource Augment = ModifierSource.Parse("augment.shield_amp");

        static StatSheet Sheet()
        {
            var sheet = new StatSheet();

            sheet.SetBase(StatId.Damage, 100f);

            return sheet;
        }

        [Test]
        public void ANewSheet_StartsAtTheCatalogBaseValues()
        {
            var sheet = new StatSheet();

            foreach (var definition in StatCatalog.Default.Definitions)
            {
                Assert.That(sheet.Get(definition.Id), Is.EqualTo(definition.BaseValue), definition.Key);
            }
        }

        /// <summary>
        /// <c>(base + flatAdd) × (1 + Σ percentAdd) × Π (1 + percentMult)</c> — the one calculation
        /// every item in the game is balanced against.
        /// </summary>
        [Test]
        public void Resolution_FollowsTheFixedOperationOrder()
        {
            var sheet = Sheet();

            sheet.Add(StatModifier.Flat(StatId.Damage, 50f, Ring));
            sheet.Add(StatModifier.PercentAdd(StatId.Damage, 0.2f, Ring));
            sheet.Add(StatModifier.PercentAdd(StatId.Damage, 0.1f, Augment));
            sheet.Add(StatModifier.PercentMult(StatId.Damage, 0.5f, Augment));

            // (100 + 50) × (1 + 0.3) × 1.5
            Assert.That(sheet.Get(StatId.Damage), Is.EqualTo(292.5f).Within(1e-3f));
        }

        [Test]
        public void PercentAdd_StacksAdditively()
        {
            var sheet = Sheet();

            sheet.Add(StatModifier.PercentAdd(StatId.Damage, 0.15f, Ring));
            sheet.Add(StatModifier.PercentAdd(StatId.Damage, 0.15f, Augment));

            Assert.That(sheet.Get(StatId.Damage), Is.EqualTo(130f).Within(1e-3f));
        }

        [Test]
        public void PercentMult_StacksMultiplicatively()
        {
            var sheet = Sheet();

            sheet.Add(StatModifier.PercentMult(StatId.Damage, 0.15f, Ring));
            sheet.Add(StatModifier.PercentMult(StatId.Damage, 0.15f, Augment));

            Assert.That(sheet.Get(StatId.Damage), Is.EqualTo(132.25f).Within(1e-3f));
        }

        /// <summary>
        /// The property that makes augment pick order irrelevant, and makes a replicated modifier
        /// list safe to rebuild in whatever sequence it arrives in.
        /// </summary>
        [Test]
        public void TheOrderModifiersArriveIn_DoesNotChangeTheResult()
        {
            var modifiers = new List<StatModifier>
            {
                StatModifier.Flat(StatId.Damage, 12f, Ring),
                StatModifier.PercentMult(StatId.Damage, 0.25f, Ring),
                StatModifier.PercentAdd(StatId.Damage, 0.4f, Augment),
                StatModifier.Flat(StatId.Damage, -3f, Augment),
                StatModifier.PercentAdd(StatId.Damage, 0.05f, SecondRing)
            };

            var expected = Resolve(modifiers);

            for (var rotation = 1; rotation < modifiers.Count; rotation++)
            {
                var rotated = new List<StatModifier>(modifiers.GetRange(rotation, modifiers.Count - rotation));

                rotated.AddRange(modifiers.GetRange(0, rotation));

                Assert.That(Resolve(rotated), Is.EqualTo(expected).Within(1e-3f), $"rotated by {rotation}");
            }

            var reversed = new List<StatModifier>(modifiers);

            reversed.Reverse();

            Assert.That(Resolve(reversed), Is.EqualTo(expected).Within(1e-3f), "reversed");
        }

        /// <summary>Listed as an invariant in the working rules, and the reason removal is by source.</summary>
        [Test]
        public void RemovingASource_LeavesTheSheetExactlyAsItWas()
        {
            var sheet = Sheet();

            sheet.Add(StatModifier.Flat(StatId.Damage, 20f, Augment));
            sheet.Add(StatModifier.PercentAdd(StatId.MaxHealth, 0.3f, Augment));

            var damageBefore = sheet.Get(StatId.Damage);
            var healthBefore = sheet.Get(StatId.MaxHealth);

            sheet.Add(StatModifier.Flat(StatId.Damage, 999f, Ring));
            sheet.Add(StatModifier.PercentMult(StatId.MaxHealth, 2f, Ring));
            sheet.Add(StatModifier.Flat(StatId.Haste, 40f, Ring));

            Assert.That(sheet.RemoveSource(Ring), Is.EqualTo(3));

            Assert.That(sheet.Get(StatId.Damage), Is.EqualTo(damageBefore).Within(1e-4f));
            Assert.That(sheet.Get(StatId.MaxHealth), Is.EqualTo(healthBefore).Within(1e-4f));
            Assert.That(sheet.Get(StatId.Haste), Is.EqualTo(0f));
        }

        /// <summary>Selling one of two identical rings must cost exactly one ring worth of stats.</summary>
        [Test]
        public void RemovingOneInstance_LeavesTheOtherCopyAlone()
        {
            var sheet = Sheet();

            sheet.Add(StatModifier.Flat(StatId.Damage, 10f, Ring));
            sheet.Add(StatModifier.Flat(StatId.Damage, 10f, SecondRing));

            sheet.RemoveSource(SecondRing);

            Assert.That(sheet.Get(StatId.Damage), Is.EqualTo(110f).Within(1e-4f));
            Assert.That(sheet.CountFrom(Ring), Is.EqualTo(1));
        }

        [Test]
        public void RemovingContent_TakesEveryInstanceOfIt()
        {
            var sheet = Sheet();

            sheet.Add(StatModifier.Flat(StatId.Damage, 10f, Ring));
            sheet.Add(StatModifier.Flat(StatId.Damage, 10f, SecondRing));
            sheet.Add(StatModifier.Flat(StatId.Damage, 7f, Augment));

            Assert.That(sheet.RemoveContent(Ring.Content), Is.EqualTo(2));
            Assert.That(sheet.Get(StatId.Damage), Is.EqualTo(107f).Within(1e-4f));
        }

        [Test]
        public void RemovingASourceThatAppliedNothing_ChangesNothing()
        {
            var sheet = Sheet();

            sheet.Add(StatModifier.Flat(StatId.Damage, 10f, Ring));

            Assert.That(sheet.RemoveSource(Augment), Is.EqualTo(0));
            Assert.That(sheet.Get(StatId.Damage), Is.EqualTo(110f).Within(1e-4f));
        }

        [Test]
        public void ChangingAStat_AnnouncesThatStatAndNoOther()
        {
            var sheet = Sheet();
            var announced = new List<StatId>();

            sheet.Changed += stat => announced.Add(stat);

            sheet.Add(StatModifier.Flat(StatId.Damage, 5f, Ring));

            Assert.That(announced, Is.EqualTo(new[] { StatId.Damage }));
        }

        [Test]
        public void AChangedBaseValue_ShowsUpInTheResolvedStat()
        {
            var sheet = Sheet();

            sheet.Add(StatModifier.PercentAdd(StatId.Damage, 1f, Ring));

            Assert.That(sheet.Get(StatId.Damage), Is.EqualTo(200f).Within(1e-4f));

            sheet.SetBase(StatId.Damage, 50f);

            Assert.That(sheet.Get(StatId.Damage), Is.EqualTo(100f).Within(1e-4f));
        }

        [Test]
        public void AStatWithAMinimum_StopsThere()
        {
            var sheet = new StatSheet();
            var floor = StatCatalog.Default[StatId.MaxHealth].Minimum;

            sheet.Add(StatModifier.Flat(StatId.MaxHealth, -100000f, Ring));

            Assert.That(sheet.Get(StatId.MaxHealth), Is.EqualTo(floor));
        }

        [Test]
        public void ALinearStat_HasNoDerivation()
        {
            var sheet = Sheet();

            Assert.That(sheet.Effect(StatId.Damage), Is.EqualTo(sheet.Get(StatId.Damage)));
        }

        [Test]
        public void ADerivedStat_StaysBoundedNoMatterWhatIsStacked()
        {
            var sheet = new StatSheet();

            for (var i = 0; i < 50; i++)
            {
                sheet.Add(StatModifier.Flat(StatId.DamageReduction, 500f, ModifierSource.Parse("item.plate", i)));
            }

            Assert.That(sheet.Get(StatId.DamageReduction), Is.EqualTo(25000f).Within(1f));
            Assert.That(sheet.Effect(StatId.DamageReduction), Is.LessThan(1f));
        }

        [Test]
        public void Clearing_TakesEveryModifierAndKeepsTheBases()
        {
            var sheet = Sheet();

            sheet.Add(StatModifier.Flat(StatId.Damage, 40f, Ring));
            sheet.Add(StatModifier.Flat(StatId.MaxHealth, 40f, Augment));

            sheet.Clear();

            Assert.That(sheet.Modifiers, Is.Empty);
            Assert.That(sheet.Get(StatId.Damage), Is.EqualTo(100f));
            Assert.That(sheet.Get(StatId.MaxHealth), Is.EqualTo(StatCatalog.Default[StatId.MaxHealth].BaseValue));
        }

        static float Resolve(IEnumerable<StatModifier> modifiers)
        {
            var sheet = Sheet();

            sheet.Add(modifiers);

            return sheet.Get(StatId.Damage);
        }
    }
}
