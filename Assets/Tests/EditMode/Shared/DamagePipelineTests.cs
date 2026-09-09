using System.Collections.Generic;
using HypeSwarm.Shared.Combat;
using HypeSwarm.Shared.Stats;
using NUnit.Framework;

namespace HypeSwarm.Shared.Tests
{
    /// <summary>
    /// The choke point every point of damage passes through: dodge, then damage reduction, and the
    /// two flags that skip them.
    /// </summary>
    /// <remarks>
    /// Every number here is supplied by the test. The soft cap below is not the shipped one and is not
    /// meant to be — what has a right answer is that a soft cap worth of reduction removes exactly
    /// half, whatever that number happens to be this week.
    /// </remarks>
    [TestFixture]
    public sealed class DamagePipelineTests
    {
        const float SoftCap = 100f;

        static readonly ModifierSource Hit = ModifierSource.Parse("test.hit");

        /// <summary>A catalog whose derived stats use this fixture soft cap rather than the shipped one.</summary>
        static StatCatalog Catalog()
        {
            var entries = new List<StatDefinition>();

            foreach (var definition in StatCatalog.Default.Definitions)
            {
                entries.Add(definition.With(definition.BaseValue, definition.IsDerived ? SoftCap : definition.SoftCap));
            }

            return new StatCatalog(entries);
        }

        static StatSheet Sheet(params StatModifier[] modifiers)
        {
            var sheet = new StatSheet(Catalog());

            sheet.Add(modifiers);

            return sheet;
        }

        static DamagePacket Packet(float amount, DamageFlags flags = DamageFlags.None)
        {
            return new DamagePacket(amount, Hit, flags);
        }

        /// <summary>A roll that never dodges, for every test that is not about dodging.</summary>
        const float NeverDodges = 0.999f;

        [Test]
        public void WithNoDefences_TheFullAmountLands()
        {
            var result = DamagePipeline.Mitigate(Packet(100f), null, Sheet(), NeverDodges);

            Assert.That(result.Amount, Is.EqualTo(100f));
            Assert.That(result.Dodged, Is.False);
        }

        /// <summary>
        /// The §5.6.2 promise made concrete: a soft cap worth of a derived stat is worth exactly half
        /// of it. This is the number every tuning conversation about damage reduction starts from.
        /// </summary>
        [Test]
        public void ASoftCapOfDamageReduction_RemovesHalfTheDamage()
        {
            var defender = Sheet(StatModifier.Flat(StatId.DamageReduction, SoftCap, Hit));

            var result = DamagePipeline.Mitigate(Packet(100f), null, defender, NeverDodges);

            Assert.That(result.Amount, Is.EqualTo(50f).Within(0.001f));
            Assert.That(result.ReductionFraction, Is.EqualTo(0.5f).Within(0.001f));
        }

        /// <summary>
        /// The invariant behind never capping a stat: no amount of damage reduction removes all of a
        /// hit, so a stacking build is always still killable and no formula divides by zero.
        /// </summary>
        [Test]
        public void NoAmountOfDamageReduction_EverRemovesAllTheDamage()
        {
            foreach (var reduction in new[] { 1000f, 100000f, 1e12f, float.MaxValue })
            {
                var defender = Sheet(StatModifier.Flat(StatId.DamageReduction, reduction, Hit));

                var result = DamagePipeline.Mitigate(Packet(1000f), null, defender, NeverDodges);

                Assert.That(result.Amount, Is.GreaterThan(0f), $"{reduction} reduction removed everything");
            }
        }

        [Test]
        public void MoreDamageReduction_NeverLetsMoreDamageThrough()
        {
            var previous = float.MaxValue;

            for (var reduction = 0f; reduction < 2000f; reduction += 37f)
            {
                var defender = Sheet(StatModifier.Flat(StatId.DamageReduction, reduction, Hit));

                var amount = DamagePipeline.Mitigate(Packet(500f), null, defender, NeverDodges).Amount;

                Assert.That(amount, Is.LessThanOrEqualTo(previous), $"at {reduction} reduction");

                previous = amount;
            }
        }

        [Test]
        public void TrueDamage_SkipsReductionEntirely()
        {
            var defender = Sheet(StatModifier.Flat(StatId.DamageReduction, SoftCap * 10f, Hit));

            var result = DamagePipeline.Mitigate(Packet(100f, DamageFlags.IgnoresReduction), null, defender, NeverDodges);

            Assert.That(result.Amount, Is.EqualTo(100f));
        }

        // --- Penetration -------------------------------------------------------------------

        [Test]
        public void PenetrationRemovesReduction_BeforeItIsDerived()
        {
            Assert.That(DamagePipeline.EffectiveReduction(150f, 50f), Is.EqualTo(100f));
        }

        /// <summary>
        /// Penetration past what the target has is wasted, not amplification. Otherwise one
        /// penetration item would be the best damage item in the game against an unarmoured horde.
        /// </summary>
        [Test]
        public void PenetrationCannotPushADefenderBelowZero()
        {
            Assert.That(DamagePipeline.EffectiveReduction(20f, 500f), Is.EqualTo(0f));
        }

        /// <summary>
        /// A shred debuff is allowed to do what penetration is not, because it had to be applied to
        /// that target first.
        /// </summary>
        [Test]
        public void ADefenderAlreadyBelowZero_KeepsItsAmplification()
        {
            var defender = Sheet(StatModifier.Flat(StatId.DamageReduction, -SoftCap, Hit));

            var result = DamagePipeline.Mitigate(Packet(100f), null, defender, NeverDodges);

            Assert.That(result.Amount, Is.GreaterThan(100f));
            Assert.That(DamagePipeline.EffectiveReduction(-50f, 500f), Is.EqualTo(-50f), "penetration must not soften a shred");
        }

        [Test]
        public void AnAttackerWithPenetration_GetsMoreThroughThanOneWithout()
        {
            var defender = Sheet(StatModifier.Flat(StatId.DamageReduction, SoftCap, Hit));
            var attacker = Sheet(StatModifier.Flat(StatId.Penetration, SoftCap / 2f, Hit));

            var without = DamagePipeline.Mitigate(Packet(100f), null, defender, NeverDodges).Amount;
            var with = DamagePipeline.Mitigate(Packet(100f), attacker, defender, NeverDodges).Amount;

            Assert.That(with, Is.GreaterThan(without));
        }

        /// <summary>
        /// The dependency runs one way (§5.6.5): the packet arrives with the attacker scaling already
        /// applied, so a pipeline that read the damage stat itself would be applying it twice.
        /// </summary>
        [Test]
        public void TheAttackerDamageStat_IsNotReadHere()
        {
            var attacker = Sheet(StatModifier.Flat(StatId.Damage, 1000f, Hit));

            var result = DamagePipeline.Mitigate(Packet(10f), attacker, Sheet(), NeverDodges);

            Assert.That(result.Amount, Is.EqualTo(10f));
        }

        // --- Dodge -------------------------------------------------------------------------

        [Test]
        public void ARollUnderTheChance_AvoidsTheHitEntirely()
        {
            var defender = Sheet(StatModifier.Flat(StatId.Dodge, SoftCap, Hit));

            var result = DamagePipeline.Mitigate(Packet(100f), null, defender, 0.4f);

            Assert.That(result.Dodged, Is.True);
            Assert.That(result.Amount, Is.EqualTo(0f));
            Assert.That(result.Requested, Is.EqualTo(100f), "the packet still knows what it asked for");
        }

        [Test]
        public void ARollOverTheChance_LetsTheHitThrough()
        {
            var defender = Sheet(StatModifier.Flat(StatId.Dodge, SoftCap, Hit));

            var result = DamagePipeline.Mitigate(Packet(100f), null, defender, 0.6f);

            Assert.That(result.Dodged, Is.False);
            Assert.That(result.Amount, Is.EqualTo(100f));
        }

        [Test]
        public void WithNoDodgeStat_NoRollEverAvoidsAnything()
        {
            var result = DamagePipeline.Mitigate(Packet(100f), null, Sheet(), 0f);

            Assert.That(result.Dodged, Is.False);
        }

        /// <summary>
        /// Rolling dodge on every tick of a burn turns a defensive stat into a coin flip the player
        /// cannot read, so ticks opt out.
        /// </summary>
        [Test]
        public void UndodgeableDamage_IsNotDodgedAtAnyChance()
        {
            var defender = Sheet(StatModifier.Flat(StatId.Dodge, SoftCap * 100f, Hit));

            var result = DamagePipeline.Mitigate(Packet(100f, DamageFlags.Undodgeable), null, defender, 0f);

            Assert.That(result.Dodged, Is.False);
            Assert.That(result.Amount, Is.GreaterThan(0f));
        }

        // --- Degenerate inputs ---------------------------------------------------------------

        [Test]
        public void ADefenderWithNoStatSheet_TakesTheFullAmount()
        {
            var result = DamagePipeline.Mitigate(Packet(100f), null, null, 0f);

            Assert.That(result.Amount, Is.EqualTo(100f));
        }

        [Test]
        public void APacketForNothing_DoesNothing()
        {
            Assert.That(DamagePipeline.Mitigate(Packet(0f), null, Sheet(), 0f).Amount, Is.EqualTo(0f));
            Assert.That(DamagePipeline.Mitigate(Packet(-50f), null, Sheet(), 0f).Amount, Is.EqualTo(0f));
            Assert.That(DamagePipeline.Mitigate(Packet(float.NaN), null, Sheet(), 0f).Amount, Is.EqualTo(0f));
        }

        // --- The choke point itself -----------------------------------------------------------

        sealed class Dummy : IDamageable
        {
            public int Hits;

            public bool IsDead { get; set; }

            public DamageResult TakeDamage(in DamagePacket packet)
            {
                Hits++;

                return DamageResult.None;
            }
        }

        [Test]
        public void ApplyingToADeadTarget_DoesNotReachIt()
        {
            var dummy = new Dummy { IsDead = true };

            DamagePipeline.Apply(dummy, Packet(100f));

            Assert.That(dummy.Hits, Is.Zero);
        }

        [Test]
        public void ApplyingToNothing_IsNotAnError()
        {
            Assert.That(DamagePipeline.Apply(null, Packet(100f)).Dealt, Is.EqualTo(0f));
        }

        [Test]
        public void ApplyingToALivingTarget_ReachesItExactlyOnce()
        {
            var dummy = new Dummy();

            DamagePipeline.Apply(dummy, Packet(100f));

            Assert.That(dummy.Hits, Is.EqualTo(1));
        }
    }
}
