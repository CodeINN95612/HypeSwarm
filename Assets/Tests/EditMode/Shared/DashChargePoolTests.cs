using HypeSwarm.Shared.Movement;
using NUnit.Framework;

namespace HypeSwarm.Shared.Tests
{
    /// <summary>
    /// The charge pool has two behaviours the spec names explicitly (§5.5.3) and both are the kind
    /// that a plausible refactor silently replaces with something that looks the same from a
    /// distance. These pin them.
    /// </summary>
    [TestFixture]
    public sealed class DashChargePoolTests
    {
        const float Cooldown = 4f;

        static DashChargePool Pool(int capacity = 3, float cooldown = Cooldown, float lockout = 0f)
        {
            return new DashChargePool(capacity, cooldown, lockout);
        }

        static void Tick(DashChargePool pool, float seconds, float step = 0.02f)
        {
            var elapsed = 0f;

            while (elapsed < seconds)
            {
                var slice = System.Math.Min(step, seconds - elapsed);
                pool.Tick(slice);
                elapsed += slice;
            }
        }

        [Test]
        public void NewPool_StartsFull()
        {
            var pool = Pool();

            Assert.That(pool.Available, Is.EqualTo(pool.Capacity));
            Assert.That(pool.CanSpend, Is.True);
        }

        [Test]
        public void Spending_RemovesOneCharge()
        {
            var pool = Pool();

            Assert.That(pool.TrySpend(), Is.True);
            Assert.That(pool.Available, Is.EqualTo(2));
        }

        [Test]
        public void Spending_IsRefusedWhenEmpty()
        {
            var pool = Pool(capacity: 1);

            Assert.That(pool.TrySpend(), Is.True);
            Assert.That(pool.TrySpend(), Is.False, "an empty pool must refuse rather than go negative");
            Assert.That(pool.Available, Is.Zero);
        }

        [Test]
        public void ZeroCapacity_NeverSpends()
        {
            var pool = Pool(capacity: 0);

            Assert.That(pool.CanSpend, Is.False);
            Assert.That(pool.TrySpend(), Is.False);
        }

        /// <summary>
        /// The property the spec asks for: charges recharge in parallel, not in a queue. Spending
        /// three and waiting one cooldown returns all three. The sequential alternative would return
        /// the last one at three times the cooldown, which is a completely different ability.
        /// </summary>
        [Test]
        public void SpentCharges_RechargeIndependently_NotInSequence()
        {
            var pool = Pool();

            Assert.That(pool.TrySpend(), Is.True);
            Assert.That(pool.TrySpend(), Is.True);
            Assert.That(pool.TrySpend(), Is.True);

            Tick(pool, Cooldown - 0.5f);
            Assert.That(pool.Available, Is.Zero, "nothing is due back yet");

            Tick(pool, 1f);
            Assert.That(pool.Available, Is.EqualTo(3),
                "all three were spent together, so all three return together; " +
                "a sequential pool would have returned one");
        }

        [Test]
        public void ChargesSpentAtDifferentTimes_ReturnAtDifferentTimes()
        {
            var pool = Pool();

            pool.TrySpend();          // due back at t = 4
            Tick(pool, 1f);
            pool.TrySpend();          // due back at t = 5

            Tick(pool, 2.5f);         // t = 3.5
            Assert.That(pool.Available, Is.EqualTo(1), "one charge was never spent");

            Tick(pool, 1f);           // t = 4.5
            Assert.That(pool.Available, Is.EqualTo(2), "the first spend is back, the second is not");

            Tick(pool, 1f);           // t = 5.5
            Assert.That(pool.Available, Is.EqualTo(3));
        }

        /// <summary>
        /// The lockout exists so one press cannot become two dashes. Without it, a held input or a
        /// duplicated event spends the escape the player was saving.
        /// </summary>
        [Test]
        public void Lockout_RefusesASecondSpendImmediatelyAfterTheFirst()
        {
            var pool = Pool(lockout: 0.2f);

            Assert.That(pool.TrySpend(), Is.True);
            Assert.That(pool.TrySpend(), Is.False, "the lockout must swallow the second spend");
            Assert.That(pool.Available, Is.EqualTo(2), "and must not consume a charge doing so");
        }

        [Test]
        public void Lockout_ExpiresAndAllowsTheNextSpend()
        {
            var pool = Pool(lockout: 0.2f);

            pool.TrySpend();
            Tick(pool, 0.21f);

            Assert.That(pool.TrySpend(), Is.True);
        }

        /// <summary>
        /// Cooldowns must not drift with the frame rate. Subtracting a thousand small steps has to
        /// arrive where subtracting one large step arrives, or the same ability comes back at a
        /// different time on a faster machine.
        /// </summary>
        [Test]
        public void Recharge_DoesNotDriftWithTickSize()
        {
            var coarse = Pool();
            var fine = Pool();

            coarse.TrySpend();
            fine.TrySpend();

            coarse.Tick(2f);
            Tick(fine, 2f, step: 0.001f);

            Assert.That(fine.NextChargeIn, Is.EqualTo(coarse.NextChargeIn).Within(0.01f));
        }

        [Test]
        public void TickingAFullPool_DoesNotAccumulateSpareCharges()
        {
            var pool = Pool();

            Tick(pool, Cooldown * 10f);

            Assert.That(pool.Available, Is.EqualTo(pool.Capacity));
        }

        [Test]
        public void NextChargeIn_IsZeroWhenAChargeIsReady_AndCountsDownOtherwise()
        {
            var pool = Pool(capacity: 1);

            Assert.That(pool.NextChargeIn, Is.Zero);

            pool.TrySpend();
            Assert.That(pool.NextChargeIn, Is.EqualTo(Cooldown).Within(0.001f));

            Tick(pool, 1f);
            Assert.That(pool.NextChargeIn, Is.EqualTo(Cooldown - 1f).Within(0.01f));
        }

        [Test]
        public void Refill_ReturnsEveryChargeAndClearsTheLockout()
        {
            var pool = Pool(lockout: 0.5f);

            pool.TrySpend();
            pool.Refill();

            Assert.That(pool.Available, Is.EqualTo(pool.Capacity));
            Assert.That(pool.LockoutRemaining, Is.Zero);
        }

        /// <summary>
        /// Charge count is a number someone will drag in the inspector while the game is running,
        /// which is the point of putting it there.
        /// </summary>
        [Test]
        public void Configure_ToALargerCapacity_MakesTheNewChargesAvailable()
        {
            var pool = Pool(capacity: 2);

            pool.Configure(4, Cooldown, 0f);

            Assert.That(pool.Capacity, Is.EqualTo(4));
            Assert.That(pool.Available, Is.EqualTo(4));
        }

        [Test]
        public void Configure_WithUnchangedValues_LeavesCooldownsRunning()
        {
            var pool = Pool();

            pool.TrySpend();
            pool.Configure(3, Cooldown, 0f);

            Assert.That(pool.Available, Is.EqualTo(2), "reapplying the same settings must not refill");
        }
    }
}
