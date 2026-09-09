using System.Collections.Generic;
using HypeSwarm.Shared.Combat;
using NUnit.Framework;

namespace HypeSwarm.Shared.Tests
{
    /// <summary>
    /// The combat random stream. Seeded, so a dodge that happened once can be made to happen again.
    /// </summary>
    /// <remarks>
    /// An unseeded random path is a flaky test and an unreproducible bug report, which is why this
    /// exists at all rather than the code reaching for the global generator.
    /// </remarks>
    [TestFixture]
    public sealed class DamageRandomTests
    {
        static List<float> Draw(DamageRandom random, int count)
        {
            var values = new List<float>(count);

            for (var i = 0; i < count; i++)
            {
                values.Add(random.NextFloat());
            }

            return values;
        }

        [Test]
        public void TheSameSeed_ProducesTheSameSequence()
        {
            Assert.That(Draw(new DamageRandom(12345u), 64), Is.EqualTo(Draw(new DamageRandom(12345u), 64)));
        }

        [Test]
        public void DifferentSeeds_DoNotProduceTheSameSequence()
        {
            Assert.That(Draw(new DamageRandom(1u), 64), Is.Not.EqualTo(Draw(new DamageRandom(2u), 64)));
        }

        /// <summary>
        /// A roll is compared against a chance in <c>[0, 1)</c>, so a roll outside that range is a
        /// dodge stat that either never works or always does.
        /// </summary>
        [Test]
        public void EveryRollLandsInTheUnitInterval()
        {
            var random = new DamageRandom(98765u);

            for (var i = 0; i < 100000; i++)
            {
                var roll = random.NextFloat();

                Assert.That(roll, Is.GreaterThanOrEqualTo(0f).And.LessThan(1f));
            }
        }

        /// <summary>Xorshift is stuck forever if its state ever reaches zero, seed included.</summary>
        [Test]
        public void ASeedOfZero_StillProducesAStream()
        {
            var random = new DamageRandom(0u);

            Assert.That(Draw(random, 16), Is.Unique);
        }

        /// <summary>
        /// Entities spawn with consecutive net ids. Seeding from one directly would start neighbouring
        /// enemies one step apart in the same sequence, so the first few rolls would agree.
        /// </summary>
        [Test]
        public void ConsecutiveNetIds_DoNotProduceNeighbouringStreams()
        {
            var first = Draw(new DamageRandom(DamageRandom.SeedFrom(1u)), 8);
            var second = Draw(new DamageRandom(DamageRandom.SeedFrom(2u)), 8);

            Assert.That(first[0], Is.Not.EqualTo(second[0]));
            Assert.That(first, Is.Not.EqualTo(second));
        }

        [Test]
        public void TheSameNetIdAndRunSeed_SeedTheSameStream()
        {
            Assert.That(DamageRandom.SeedFrom(42u, 7u), Is.EqualTo(DamageRandom.SeedFrom(42u, 7u)));
            Assert.That(DamageRandom.SeedFrom(42u, 7u), Is.Not.EqualTo(DamageRandom.SeedFrom(42u, 8u)));
        }

        /// <summary>
        /// Not a distribution test — this is the coarse check that a stream used for dodge does not
        /// sit in one half of the range, which is the failure a broken shift would produce.
        /// </summary>
        [Test]
        public void RollsCoverBothHalvesOfTheRange()
        {
            var random = new DamageRandom(DamageRandom.SeedFrom(1234u));
            var low = 0;

            for (var i = 0; i < 10000; i++)
            {
                if (random.NextFloat() < 0.5f)
                {
                    low++;
                }
            }

            Assert.That(low, Is.GreaterThan(4000).And.LessThan(6000));
        }
    }
}
