using HypeSwarm.Shared.Stats;
using NUnit.Framework;

namespace HypeSwarm.Shared.Tests
{
    /// <summary>
    /// The asymptotic derivation from §5.6.2. The promise it makes — a stat that is never capped and
    /// an effect that never reaches one — is the kind that holds for every number anyone tries in
    /// practice and then fails on the one a player actually reaches in hour three.
    /// </summary>
    [TestFixture]
    public sealed class StatCurveTests
    {
        const float SoftCap = 100f;

        [Test]
        public void NoStat_IsNoEffect()
        {
            Assert.That(StatCurve.Diminishing(0f, SoftCap), Is.EqualTo(0f));
        }

        [Test]
        public void AtTheSoftCap_TheEffectIsHalf()
        {
            Assert.That(StatCurve.Diminishing(SoftCap, SoftCap), Is.EqualTo(0.5f).Within(1e-5f));
        }

        [Test]
        public void TheEffectRises_WithEveryPointOfTheStat()
        {
            var previous = StatCurve.Diminishing(0f, SoftCap);

            for (var value = 1f; value < 5000f; value *= 1.5f)
            {
                var current = StatCurve.Diminishing(value, SoftCap);

                Assert.That(current, Is.GreaterThan(previous), $"at {value}");

                previous = current;
            }
        }

        [Test]
        public void TheEffectNeverReachesOne_AtAnyInput()
        {
            var absurd = new[]
            {
                1000f, 100000f, 1e12f, 1e30f, float.MaxValue, float.PositiveInfinity
            };

            foreach (var value in absurd)
            {
                Assert.That(StatCurve.Diminishing(value, SoftCap), Is.LessThan(1f), $"at {value}");
            }
        }

        [Test]
        public void TheEffectNeverReachesMinusOne_AtAnyInput()
        {
            var absurd = new[]
            {
                -1000f, -100000f, -1e30f, float.MinValue, float.NegativeInfinity
            };

            foreach (var value in absurd)
            {
                Assert.That(StatCurve.Diminishing(value, SoftCap), Is.GreaterThan(-1f), $"at {value}");
            }
        }

        /// <summary>A vulnerability debuff is a negative stat, and it must not fall off the curve.</summary>
        [Test]
        public void NegativeStats_MirrorPositiveOnes()
        {
            Assert.That(
                StatCurve.Diminishing(-250f, SoftCap),
                Is.EqualTo(-StatCurve.Diminishing(250f, SoftCap)).Within(1e-6f));
        }

        /// <summary>The naive formula divides by zero at exactly this input.</summary>
        [Test]
        public void MinusTheSoftCap_DoesNotDivideByZero()
        {
            var effect = StatCurve.Diminishing(-SoftCap, SoftCap);

            Assert.That(float.IsNaN(effect), Is.False);
            Assert.That(float.IsInfinity(effect), Is.False);
            Assert.That(effect, Is.EqualTo(-0.5f).Within(1e-5f));
        }

        [Test]
        public void AMisconfiguredSoftCap_StillProducesABoundedEffect()
        {
            foreach (var badCap in new[] { 0f, -1f, -100f })
            {
                var effect = StatCurve.Diminishing(50f, badCap);

                Assert.That(effect, Is.LessThan(1f).And.GreaterThanOrEqualTo(0f), $"soft cap {badCap}");
            }
        }

        [Test]
        public void Reduction_TurnsTheEffectIntoAMultiplier()
        {
            Assert.That(StatCurve.Reduction(SoftCap, SoftCap), Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(StatCurve.Reduction(0f, SoftCap), Is.EqualTo(1f));

            // Negative reduction is vulnerability: more damage taken, not less.
            Assert.That(StatCurve.Reduction(-SoftCap, SoftCap), Is.GreaterThan(1f));
        }

        [Test]
        public void Required_AnswersHowMuchStatBuysAnEffect()
        {
            foreach (var effect in new[] { 0.1f, 0.25f, 0.5f, 0.75f, 0.9f, -0.4f })
            {
                var value = StatCurve.Required(effect, SoftCap);

                Assert.That(
                    StatCurve.Diminishing(value, SoftCap),
                    Is.EqualTo(effect).Within(1e-4f),
                    $"for {effect}");
            }
        }

        [Test]
        public void Required_SaysInfinityForAnEffectNoStatCanReach()
        {
            Assert.That(StatCurve.Required(1f, SoftCap), Is.EqualTo(float.PositiveInfinity));
            Assert.That(StatCurve.Required(1.5f, SoftCap), Is.EqualTo(float.PositiveInfinity));
        }
    }
}
