using System.Collections.Generic;
using HypeSwarm.Shared.Combat;
using HypeSwarm.Shared.Stats;
using NUnit.Framework;

namespace HypeSwarm.Shared.Tests
{
    /// <summary>
    /// The health pool: subtraction, shields in front of it, the death boundary, regeneration, and the
    /// maximum moving under it while the player is alive.
    /// </summary>
    /// <remarks>
    /// No number here is a balance number. Regeneration rates and revive fractions are supplied by the
    /// test, because what has a right answer is that regeneration waits for its delay and that a
    /// revive returns the share it was asked for — not what that share is this week.
    /// </remarks>
    [TestFixture]
    public sealed class HealthPoolTests
    {
        static readonly ModifierSource Hit = ModifierSource.Parse("test.hit");
        static readonly ModifierSource Barrier = ModifierSource.Parse("test.barrier");

        static DamageMitigation Landing(float amount) => DamageMitigation.Unmitigated(amount);

        [Test]
        public void APoolStartsFull()
        {
            var pool = new HealthPool(250f);

            Assert.That(pool.Current, Is.EqualTo(250f));
            Assert.That(pool.Max, Is.EqualTo(250f));
            Assert.That(pool.IsFull, Is.True);
            Assert.That(pool.IsDead, Is.False);
        }

        [Test]
        public void DamageComesOffHealth()
        {
            var pool = new HealthPool(100f);

            var result = pool.Apply(Landing(30f));

            Assert.That(pool.Current, Is.EqualTo(70f));
            Assert.That(result.DealtToHealth, Is.EqualTo(30f));
            Assert.That(result.Killed, Is.False);
        }

        [Test]
        public void ShieldsAreSpentBeforeHealth()
        {
            var pool = new HealthPool(100f);

            pool.Shields.Add(Barrier, 40f, 10f);

            var result = pool.Apply(Landing(60f));

            Assert.That(result.AbsorbedByShield, Is.EqualTo(40f));
            Assert.That(result.DealtToHealth, Is.EqualTo(20f));
            Assert.That(pool.Current, Is.EqualTo(80f));
            Assert.That(pool.Shield, Is.EqualTo(0f));
        }

        [Test]
        public void AShieldLargeEnough_LeavesHealthUntouched()
        {
            var pool = new HealthPool(100f);

            pool.Shields.Add(Barrier, 200f, 10f);
            pool.Apply(Landing(150f));

            Assert.That(pool.Current, Is.EqualTo(100f));
            Assert.That(pool.Shield, Is.EqualTo(50f));
        }

        /// <summary>
        /// The one that quietly breaks lifesteal: a hit for a thousand on a target with two health
        /// must be worth two, or the best lifesteal build in the game is the one that overkills hardest.
        /// </summary>
        [Test]
        public void Overkill_IsNotCountedAsDamageDealt()
        {
            var pool = new HealthPool(100f);

            var result = pool.Apply(Landing(1000f));

            Assert.That(result.DealtToHealth, Is.EqualTo(100f));
            Assert.That(result.Overkill, Is.EqualTo(900f));
            Assert.That(result.Dealt, Is.EqualTo(100f));
        }

        [Test]
        public void HealthStopsAtZero_AndTheKillingBlowSaysSo()
        {
            var pool = new HealthPool(100f);

            var result = pool.Apply(Landing(100f));

            Assert.That(pool.Current, Is.EqualTo(0f));
            Assert.That(pool.IsDead, Is.True);
            Assert.That(result.Killed, Is.True);
        }

        [Test]
        public void ADeadPoolAnnouncesItsDeathExactlyOnce()
        {
            var pool = new HealthPool(100f);
            var deaths = 0;

            pool.Died += _ => deaths++;

            pool.Apply(Landing(500f));
            pool.Apply(Landing(500f));
            pool.Apply(Landing(500f));

            Assert.That(deaths, Is.EqualTo(1));
        }

        [Test]
        public void ADeadPoolTakesNoFurtherDamage()
        {
            var pool = new HealthPool(100f);

            pool.Apply(Landing(100f));

            var after = pool.Apply(Landing(50f));

            Assert.That(after.Dealt, Is.EqualTo(0f));
            Assert.That(after.Killed, Is.False);
        }

        [Test]
        public void DyingDropsWhateverShieldsWereUp()
        {
            var pool = new HealthPool(100f);

            pool.Shields.Add(Barrier, 10f, 10f);
            pool.Apply(Landing(500f));

            Assert.That(pool.Shield, Is.EqualTo(0f));
        }

        /// <summary>
        /// A dodge that woke every on-hit trigger in the game would make dodge worse than not having it.
        /// </summary>
        [Test]
        public void ADodgedHit_TouchesNothingAndTriggersNothing()
        {
            var pool = new HealthPool(100f);
            var damagedEvents = 0;

            pool.Damaged += _ => damagedEvents++;

            var result = pool.Apply(DamageMitigation.Avoided(80f));

            Assert.That(pool.Current, Is.EqualTo(100f));
            Assert.That(result.Dodged, Is.True);
            Assert.That(damagedEvents, Is.Zero);
        }

        // --- The maximum moving underneath -----------------------------------------------

        /// <summary>
        /// A max-health item has to be worth something the moment it is equipped, not once the player
        /// has healed into it.
        /// </summary>
        [Test]
        public void RaisingTheMaximum_GrantsTheDifferenceAsHealth()
        {
            var pool = new HealthPool(100f);

            pool.Apply(Landing(50f));
            pool.SetMax(200f);

            Assert.That(pool.Current, Is.EqualTo(150f));
        }

        /// <summary>
        /// The worst way for a temporary stat to behave: a buff expiring and killing the player it was
        /// helping. Losing maximum health clamps and nothing more.
        /// </summary>
        [Test]
        public void LoweringTheMaximum_CannotKill()
        {
            var pool = new HealthPool(1000f);

            pool.Apply(Landing(995f));
            pool.SetMax(10f);

            Assert.That(pool.IsDead, Is.False);
            Assert.That(pool.Current, Is.GreaterThan(0f));
            Assert.That(pool.Current, Is.LessThanOrEqualTo(10f));
        }

        [Test]
        public void RaisingTheMaximum_DoesNotRaiseTheDead()
        {
            var pool = new HealthPool(100f);

            pool.Apply(Landing(100f));
            pool.SetMax(500f);

            Assert.That(pool.IsDead, Is.True);
            Assert.That(pool.Current, Is.EqualTo(0f));
        }

        [Test]
        public void TheMaximumNeverReachesZero_SoAFractionIsAlwaysADivision()
        {
            var pool = new HealthPool(0f);

            pool.SetMax(-500f);

            Assert.That(pool.Max, Is.GreaterThan(0f));
            Assert.That(pool.Fraction, Is.EqualTo(1f));
        }

        // --- Healing and regeneration ------------------------------------------------------

        [Test]
        public void HealingStopsAtTheMaximum_AndReportsWhatLanded()
        {
            var pool = new HealthPool(100f);

            pool.Apply(Landing(30f));

            Assert.That(pool.Heal(100f), Is.EqualTo(30f));
            Assert.That(pool.Current, Is.EqualTo(100f));
            Assert.That(pool.Heal(10f), Is.EqualTo(0f), "a full pool has nowhere to put it");
        }

        [Test]
        public void HealingDoesNotRaiseTheDead()
        {
            var pool = new HealthPool(100f);

            pool.Apply(Landing(100f));

            Assert.That(pool.Heal(50f), Is.EqualTo(0f));
            Assert.That(pool.IsDead, Is.True);
        }

        [Test]
        public void RegenerationWaitsForItsDelay()
        {
            var pool = new HealthPool(100f);

            pool.Apply(Landing(50f));

            for (var i = 0; i < 4; i++)
            {
                pool.Tick(1f, 10f, 5f);
            }

            Assert.That(pool.Current, Is.EqualTo(50f), "regeneration started inside the delay");

            pool.Tick(1f, 10f, 5f);
            pool.Tick(1f, 10f, 5f);

            Assert.That(pool.Current, Is.GreaterThan(50f));
        }

        [Test]
        public void BeingHitStartsTheDelayAgain()
        {
            var pool = new HealthPool(100f);

            pool.Apply(Landing(50f));
            pool.Tick(4f, 10f, 5f);
            pool.Apply(Landing(1f));
            pool.Tick(4f, 10f, 5f);

            Assert.That(pool.Current, Is.EqualTo(49f));
        }

        /// <summary>
        /// Regeneration is a rate, so how often it is ticked must not change what it restores. A pool
        /// that heals more on a fast machine is a pool nobody can balance.
        /// </summary>
        [Test]
        public void RegenerationDoesNotDependOnTheStepSize()
        {
            var restored = new List<float>();

            foreach (var step in new[] { 0.03125f, 0.125f, 0.5f, 1f })
            {
                var pool = new HealthPool(1000f);

                pool.Apply(Landing(500f));

                var elapsed = 0f;

                while (elapsed + step <= 4f)
                {
                    pool.Tick(step, 10f, 0f);
                    elapsed += step;
                }

                restored.Add(pool.Current - 500f);
            }

            foreach (var amount in restored)
            {
                Assert.That(amount, Is.EqualTo(40f).Within(0.01f));
            }
        }

        [Test]
        public void WithNoRegenStat_NothingComesBack()
        {
            var pool = new HealthPool(100f);

            pool.Apply(Landing(50f));
            pool.Tick(60f, 0f, 0f);

            Assert.That(pool.Current, Is.EqualTo(50f));
        }

        [Test]
        public void ADeadPoolDoesNotRegenerate()
        {
            var pool = new HealthPool(100f);

            pool.Apply(Landing(100f));
            pool.Tick(60f, 100f, 0f);

            Assert.That(pool.Current, Is.EqualTo(0f));
            Assert.That(pool.IsDead, Is.True);
        }

        // --- Coming back -------------------------------------------------------------------

        [Test]
        public void AReviveComesBackAtTheShareItWasAsked()
        {
            var pool = new HealthPool(200f);

            pool.Apply(Landing(200f));
            pool.Revive(0.25f);

            Assert.That(pool.IsDead, Is.False);
            Assert.That(pool.Current, Is.EqualTo(50f));
        }

        [Test]
        public void RevivingSomethingAlive_ChangesNothing()
        {
            var pool = new HealthPool(200f);

            pool.Apply(Landing(150f));
            pool.Revive(1f);

            Assert.That(pool.Current, Is.EqualTo(50f));
        }

        [Test]
        public void KillingOutright_ReportsTheHealthItTook()
        {
            var pool = new HealthPool(200f);

            pool.Apply(Landing(50f));

            var result = pool.Kill();

            Assert.That(result.Killed, Is.True);
            Assert.That(result.DealtToHealth, Is.EqualTo(150f));
            Assert.That(pool.IsDead, Is.True);
        }

        [Test]
        public void KillingSomethingAlreadyDead_DoesNothing()
        {
            var pool = new HealthPool(200f);

            pool.Kill();

            Assert.That(pool.Kill().Killed, Is.False);
        }

        // --- Where the pipeline and the pool meet -------------------------------------------

        /// <summary>
        /// Reduction resolves before a shield absorbs, not after. The other order would make damage
        /// reduction worth less exactly while a shield is up, which is backwards.
        /// </summary>
        [Test]
        public void ReductionAppliesBeforeAShieldAbsorbs()
        {
            var pool = new HealthPool(100f);

            pool.Shields.Add(Barrier, 25f, 10f);

            // Half the hit removed by reduction, so a 25 shield covers all of what is left.
            var result = pool.Apply(new DamageMitigation(50f, 25f, false, 0.5f));

            Assert.That(result.AbsorbedByShield, Is.EqualTo(25f));
            Assert.That(result.DealtToHealth, Is.EqualTo(0f));
            Assert.That(pool.Current, Is.EqualTo(100f));
        }
    }
}
