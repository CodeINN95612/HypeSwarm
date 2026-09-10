using HypeSwarm.Shared.Abilities;
using NUnit.Framework;

namespace HypeSwarm.Shared.Tests
{
    /// <summary>
    /// One owner's copy of an ability: its cooldown, its charges, and how haste reaches them.
    /// </summary>
    /// <remarks>
    /// The first test here is the one that matters most, because the bug it pins is invisible in single
    /// player and obvious in a party: two instances sharing a timer because somebody put state on the
    /// definition. It is on the spec's trap list for that reason.
    /// </remarks>
    [TestFixture]
    public sealed class AbilityInstanceTests
    {
        static AbilityInstance Instance(float cooldown = 10f, int charges = 1, float lockout = 0f, int slot = 0)
        {
            return new AbilityInstance(
                new TestAbility { Cooldown = cooldown, Charges = charges, ChargeLockout = lockout },
                slot);
        }

        [Test]
        public void AnAbilityStartsReady()
        {
            var instance = Instance();

            Assert.That(instance.IsReady, Is.True);
            Assert.That(instance.CooldownRemaining, Is.Zero);
        }

        /// <summary>
        /// The trap: a ScriptableObject is one shared asset, so state kept on it would be shared by
        /// every owner. Two players on the same champion must not share a cooldown.
        /// </summary>
        [Test]
        public void TwoInstancesOfOneDefinition_HaveIndependentCooldowns()
        {
            var definition = new TestAbility { Cooldown = 10f };

            var mine = new AbilityInstance(definition);
            var theirs = new AbilityInstance(definition);

            Assert.That(mine.TryActivate(0f), Is.True);

            Assert.That(mine.IsReady, Is.False);
            Assert.That(theirs.IsReady, Is.True, "casting mine put theirs on cooldown");
        }

        [Test]
        public void CastingSpendsACharge_AndTheCooldownRunsDown()
        {
            var instance = Instance(cooldown: 4f);

            instance.TryActivate(0f);

            Assert.That(instance.CooldownRemaining, Is.EqualTo(4f));

            instance.Tick(3f);

            Assert.That(instance.CooldownRemaining, Is.EqualTo(1f));
            Assert.That(instance.IsReady, Is.False);

            instance.Tick(1f);

            Assert.That(instance.IsReady, Is.True);
        }

        [Test]
        public void AnAbilityOnCooldown_RefusesToActivate()
        {
            var instance = Instance();

            Assert.That(instance.TryActivate(0f), Is.True);
            Assert.That(instance.TryActivate(0f), Is.False);
        }

        [Test]
        public void ChargesAreSpentOneAtATime()
        {
            var instance = Instance(cooldown: 6f, charges: 3);

            Assert.That(instance.ChargesAvailable, Is.EqualTo(3));

            instance.TryActivate(0f);

            Assert.That(instance.ChargesAvailable, Is.EqualTo(2));
            Assert.That(instance.IsReady, Is.True, "two charges left is still ready");
        }

        /// <summary>
        /// The lockout, which is not a balance number: without it one frame of input spends two
        /// charges, and the player reads that as the game eating their escape (§5.5.3).
        /// </summary>
        [Test]
        public void TheLockout_StopsTwoChargesGoingAtOnce()
        {
            var instance = Instance(cooldown: 6f, charges: 2, lockout: 0.2f);

            Assert.That(instance.TryActivate(0f), Is.True);
            Assert.That(instance.TryActivate(0f), Is.False, "the lockout should have refused the second");

            instance.Tick(0.2f);

            Assert.That(instance.TryActivate(0f), Is.True);
        }

        // --- Haste ---------------------------------------------------------------------------

        [Test]
        public void HasteShortensTheCooldown()
        {
            var instance = Instance(cooldown: 10f);

            Assert.That(instance.CooldownAt(0.25f), Is.EqualTo(7.5f).Within(0.001f));
        }

        /// <summary>
        /// Haste is read when the ability is used and never again. A cooldown already running is not
        /// shortened by a buff that lands afterwards, which is what players expect and what stops a
        /// haste buff being worth more the instant after you spend everything than the instant before.
        /// </summary>
        [Test]
        public void HasteArrivingMidCooldown_DoesNotShortenIt()
        {
            var instance = Instance(cooldown: 10f);

            instance.TryActivate(0f);
            instance.Tick(1f);

            Assert.That(instance.CooldownRemaining, Is.EqualTo(9f));

            // A buff lands, and the next use is faster. This one is not.
            Assert.That(instance.CooldownAt(0.5f), Is.EqualTo(5f));
            Assert.That(instance.CooldownRemaining, Is.EqualTo(9f));
        }

        /// <summary>
        /// Haste derives asymptotically and never reaches one (§5.6.2), so no amount of it can produce
        /// a cooldown of zero. The floor below it is for an ability authored with a tiny cooldown in the
        /// first place — a cooldown under a frame would make frame rate a damage stat.
        /// </summary>
        [Test]
        public void NoAmountOfHaste_DrivesACooldownToZero()
        {
            var instance = Instance(cooldown: 10f);

            foreach (var haste in new[] { 0.5f, 0.9f, 0.999f, 0.99999994f })
            {
                Assert.That(instance.CooldownAt(haste), Is.GreaterThanOrEqualTo(AbilityInstance.MinimumCooldown));
            }
        }

        [Test]
        public void AnAbilityWithNoCooldown_StaysReady()
        {
            var instance = Instance(cooldown: 0f);

            Assert.That(instance.TryActivate(0f), Is.True);
            Assert.That(instance.IsReady, Is.True);
        }

        [Test]
        public void NegativeHaste_LengthensTheCooldown()
        {
            var instance = Instance(cooldown: 10f);

            Assert.That(instance.CooldownAt(-0.5f), Is.EqualTo(15f));
        }

        // --- Identity and refunds -------------------------------------------------------------

        /// <summary>
        /// The slot is the instance number, so one champion carrying the same ability twice applies two
        /// separable shields rather than one that refreshes itself (§9).
        /// </summary>
        [Test]
        public void TheSourceCarriesTheSlot_SoTwoCopiesAreSeparable()
        {
            var definition = new TestAbility("ability.twice");

            var first = new AbilityInstance(definition, 1);
            var second = new AbilityInstance(definition, 2);

            Assert.That(first.Source, Is.Not.EqualTo(second.Source));
            Assert.That(first.Source.Content, Is.EqualTo(second.Source.Content));
        }

        /// <summary>
        /// For a client whose predicted cast the host refused: one charge back, not a refill.
        /// </summary>
        [Test]
        public void ARefund_ReturnsOneChargeAndNoMore()
        {
            var instance = Instance(cooldown: 6f, charges: 3, lockout: 0f);

            instance.TryActivate(0f);
            instance.TryActivate(0f);

            Assert.That(instance.ChargesAvailable, Is.EqualTo(1));

            Assert.That(instance.Refund(), Is.True);
            Assert.That(instance.ChargesAvailable, Is.EqualTo(2));
        }

        [Test]
        public void RefundingAnAbilityThatOwesNothing_DoesNothing()
        {
            Assert.That(Instance().Refund(), Is.False);
        }

        [Test]
        public void RefreshingReturnsEverything()
        {
            var instance = Instance(cooldown: 30f, charges: 2);

            instance.TryActivate(0f);
            instance.Stacks = 4;
            instance.Refresh();

            Assert.That(instance.ChargesAvailable, Is.EqualTo(2));
            Assert.That(instance.Stacks, Is.Zero);
        }
    }
}
