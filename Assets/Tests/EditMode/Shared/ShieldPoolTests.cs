using System.Collections.Generic;
using HypeSwarm.Shared.Combat;
using HypeSwarm.Shared.Stats;
using NUnit.Framework;

namespace HypeSwarm.Shared.Tests
{
    /// <summary>
    /// Shield layers: what they absorb, in what order, and what is left when one ends.
    /// </summary>
    /// <remarks>
    /// A leaked shield layer is invisible until it is not — the player simply has more effective
    /// health than anyone can account for, and at horde density the list grows every second. That is
    /// why the lifecycle is tested here rather than trusted.
    /// </remarks>
    [TestFixture]
    public sealed class ShieldPoolTests
    {
        static readonly ModifierSource Barrier = ModifierSource.Parse("ability.barrier");
        static readonly ModifierSource Ward = ModifierSource.Parse("item.ward");

        [Test]
        public void AShield_AbsorbsDamageBeforeItReachesTheCaller()
        {
            var pool = new ShieldPool();

            pool.Add(Barrier, 30f, 5f);

            Assert.That(pool.Absorb(20f), Is.EqualTo(20f));
            Assert.That(pool.Total, Is.EqualTo(10f));
        }

        [Test]
        public void DamageBeyondTheShield_LeavesTheRestForHealth()
        {
            var pool = new ShieldPool();

            pool.Add(Barrier, 30f, 5f);

            Assert.That(pool.Absorb(50f), Is.EqualTo(30f));
            Assert.That(pool.Total, Is.EqualTo(0f));
            Assert.That(pool.Count, Is.EqualTo(0));
        }

        /// <summary>
        /// The order that does not waste anything: spend what is about to disappear. Newest-first
        /// would throw away the shield the player is standing in right now.
        /// </summary>
        [Test]
        public void DamageDrains_TheLayerThatExpiresSoonestFirst()
        {
            var pool = new ShieldPool();

            pool.Add(Ward, 40f, 30f);
            pool.Add(Barrier, 40f, 2f);

            pool.Absorb(40f);

            Assert.That(pool.AmountOn(Barrier), Is.EqualTo(0f), "the short shield should have gone first");
            Assert.That(pool.AmountOn(Ward), Is.EqualTo(40f));
        }

        [Test]
        public void AShieldRunsOut_AndTakesWhatWasLeftOfItselfWithIt()
        {
            var pool = new ShieldPool();

            pool.Add(Barrier, 30f, 2f);
            pool.Add(Ward, 10f, 10f);

            pool.Tick(2f);

            Assert.That(pool.Total, Is.EqualTo(10f));
            Assert.That(pool.AmountOn(Barrier), Is.EqualTo(0f));
        }

        [Test]
        public void AnExpiringShield_ReportsWhatWentUnused()
        {
            var pool = new ShieldPool();
            var wasted = new List<float>();

            pool.Expired += (_, unused) => wasted.Add(unused);

            pool.Add(Barrier, 30f, 2f);
            pool.Absorb(12f);
            pool.Tick(2f);

            Assert.That(wasted, Is.EqualTo(new[] { 18f }));
        }

        /// <summary>
        /// A shield that absorbed the last of itself did its job. Announcing it as expired would fire
        /// "your shield ran out" on the shield that worked.
        /// </summary>
        [Test]
        public void AShieldSpentByDamage_IsNotAnnouncedAsExpired()
        {
            var pool = new ShieldPool();
            var announced = 0;

            pool.Expired += (_, __) => announced++;

            pool.Add(Barrier, 30f, 5f);
            pool.Absorb(30f);

            Assert.That(announced, Is.Zero);
        }

        [Test]
        public void AShieldFromTheSameSource_RefreshesRatherThanStacks()
        {
            var pool = new ShieldPool();

            pool.Add(Barrier, 30f, 5f);
            pool.Add(Barrier, 20f, 5f);

            Assert.That(pool.Count, Is.EqualTo(1));
            Assert.That(pool.Total, Is.EqualTo(20f));
        }

        /// <summary>
        /// Two copies of one item shield separately, which is what the instance number is for.
        /// </summary>
        [Test]
        public void TwoInstancesOfOneSource_AreSeparateLayers()
        {
            var pool = new ShieldPool();
            var first = ModifierSource.Parse("item.ward", 1);
            var second = ModifierSource.Parse("item.ward", 2);

            pool.Add(first, 10f, 5f);
            pool.Add(second, 10f, 5f);

            Assert.That(pool.Total, Is.EqualTo(20f));

            pool.Remove(first);

            Assert.That(pool.Total, Is.EqualTo(10f));
        }

        [Test]
        public void RemovingBySource_TakesOnlyThatLayer()
        {
            var pool = new ShieldPool();

            pool.Add(Barrier, 30f, 5f);
            pool.Add(Ward, 10f, 5f);

            Assert.That(pool.Remove(Barrier), Is.True);
            Assert.That(pool.Total, Is.EqualTo(10f));
            Assert.That(pool.Remove(Barrier), Is.False);
        }

        [Test]
        public void ClearingDropsEverything()
        {
            var pool = new ShieldPool();

            pool.Add(Barrier, 30f, 5f);
            pool.Add(Ward, 10f, 5f);
            pool.Clear();

            Assert.That(pool.Count, Is.Zero);
            Assert.That(pool.Total, Is.EqualTo(0f));
        }

        /// <summary>An infinite shield is the one a passive keeps up until something removes it.</summary>
        [Test]
        public void AnEndlessShield_SurvivesEveryTick()
        {
            var pool = new ShieldPool();

            pool.Add(Barrier, 30f, float.PositiveInfinity);

            for (var i = 0; i < 100; i++)
            {
                pool.Tick(1f);
            }

            Assert.That(pool.Total, Is.EqualTo(30f));
        }

        [Test]
        public void NothingIsAddedForANonPositiveAmountOrDuration()
        {
            var pool = new ShieldPool();

            Assert.That(pool.Add(Barrier, 0f, 5f), Is.False);
            Assert.That(pool.Add(Barrier, 30f, 0f), Is.False);
            Assert.That(pool.Count, Is.Zero);
        }

        [Test]
        public void AbsorbingFromAnEmptyPool_TakesNothing()
        {
            var pool = new ShieldPool();

            Assert.That(pool.Absorb(50f), Is.EqualTo(0f));
            Assert.That(pool.Total, Is.EqualTo(0f));
        }

        /// <summary>
        /// The one that would go unnoticed: a rounding error accumulating into a negative total reads
        /// as a shield that heals.
        /// </summary>
        [Test]
        public void TheTotal_NeverGoesBelowZero()
        {
            var pool = new ShieldPool();

            pool.Add(Barrier, 0.1f, 5f);
            pool.Add(Ward, 0.2f, 5f);

            for (var i = 0; i < 10; i++)
            {
                pool.Absorb(0.05f);
            }

            Assert.That(pool.Total, Is.GreaterThanOrEqualTo(0f));
        }
    }
}
