using System.Collections.Generic;
using HypeSwarm.Shared.Abilities;
using NUnit.Framework;

namespace HypeSwarm.Shared.Tests
{
    /// <summary>
    /// The indexed slots and the cast state machine: cast times, channels, interruption, the passive
    /// pulse, and what movement costs while any of it is happening.
    /// </summary>
    /// <remarks>
    /// Every number here is supplied by the test. What has a right answer is that a cast resolves when
    /// its cast time is up, that a channel resolves when it is let go, that an interrupted cast still
    /// cost its cooldown, and that a passive authored as a rate keeps its rate whatever the tick
    /// interval is — not what any of those numbers are this week.
    /// </remarks>
    [TestFixture]
    public sealed class AbilityBookTests
    {
        static AbilityBook Book(params IAbilityDefinition[] abilities)
        {
            return new AbilityBook(new List<IAbilityDefinition>(abilities));
        }

        static AbilityBook Complete() => new AbilityBook(TestChampion.Complete().Abilities);

        // --- Slots ---------------------------------------------------------------------------

        [Test]
        public void SlotsAreAddressedByRole_NotByName()
        {
            var book = Complete();

            Assert.That(book.Count, Is.EqualTo(5));
            Assert.That(book.IndexOf(AbilityRole.Mobility), Is.EqualTo(3));
            Assert.That(book.Find(AbilityRole.Ultimate).Definition.Id.Value, Is.EqualTo("ability.ultimate"));
        }

        [Test]
        public void AMissingRole_IsNotAnError()
        {
            var book = Book(new TestAbility("ability.only", AbilityRole.Primary));

            Assert.That(book.IndexOf(AbilityRole.Ultimate), Is.EqualTo(-1));
            Assert.That(book.Find(AbilityRole.Ultimate), Is.Null);
            Assert.That(book[7], Is.Null, "an out-of-range slot reads as empty rather than throwing");
        }

        // --- Casting -------------------------------------------------------------------------

        [Test]
        public void AnAbilityWithNoCastTime_ResolvesImmediately()
        {
            var book = Book(new TestAbility());
            var resolved = 0;

            book.CastResolved += (_, __) => resolved++;

            Assert.That(book.TryBegin(0), Is.EqualTo(CastOutcome.Started));
            Assert.That(resolved, Is.EqualTo(1));
            Assert.That(book.IsCasting, Is.False);
        }

        /// <summary>
        /// Two announcements, in order, and the gap between them is the whole of what a cast time is. The
        /// runner hangs the start steps off the first and the effect off the second, so an ability that
        /// grants something while it is channelled depends on this ordering (§8.4).
        /// </summary>
        [Test]
        public void ACastAnnouncesItsStartBeforeItResolves()
        {
            var book = Book(new TestAbility { CastTime = 0.5f, CastCost = CastCost.Rooted });
            var order = new List<string>();

            book.CastStarted += _ => order.Add("started");
            book.CastResolved += (_, __) => order.Add("resolved");

            book.TryBegin(0);

            Assert.That(order, Is.EqualTo(new[] { "started" }));

            book.Tick(0.5f);

            Assert.That(order, Is.EqualTo(new[] { "started", "resolved" }));
        }

        [Test]
        public void ACastTime_DelaysTheEffectAndNotTheCooldown()
        {
            var book = Book(new TestAbility { CastTime = 0.5f, CastCost = CastCost.Rooted, Cooldown = 8f });
            var resolved = 0;

            book.CastResolved += (_, __) => resolved++;

            book.TryBegin(0);

            Assert.That(book.IsCasting, Is.True);
            Assert.That(resolved, Is.Zero);
            Assert.That(book[0].IsReady, Is.False, "the charge is spent when the cast starts");

            book.Tick(0.25f);

            Assert.That(resolved, Is.Zero);
            Assert.That(book.CastProgress, Is.EqualTo(0.5f).Within(0.001f));

            book.Tick(0.25f);

            Assert.That(resolved, Is.EqualTo(1));
            Assert.That(book.IsCasting, Is.False);
        }

        /// <summary>
        /// One cast at a time, and the second is refused rather than queued: an input landing a frame
        /// early must not fire when the player no longer wants it.
        /// </summary>
        [Test]
        public void ASecondCastWhileOneIsRunning_IsRefused()
        {
            var book = Book(
                new TestAbility("ability.slow") { CastTime = 1f, CastCost = CastCost.Rooted },
                new TestAbility("ability.fast", AbilityRole.Secondary));

            book.TryBegin(0);

            Assert.That(book.TryBegin(1), Is.EqualTo(CastOutcome.Busy));
            Assert.That(book[1].IsReady, Is.True, "the refused cast must not have spent anything");
        }

        [Test]
        public void CastingSomethingOnCooldown_IsRefused()
        {
            var book = Book(new TestAbility { Cooldown = 5f });

            book.TryBegin(0);

            Assert.That(book.TryBegin(0), Is.EqualTo(CastOutcome.OnCooldown));
        }

        [Test]
        public void CastingAnEmptySlot_SaysSo()
        {
            Assert.That(Book().TryBegin(0), Is.EqualTo(CastOutcome.UnknownSlot));
        }

        /// <summary>
        /// A passive runs itself on a tick and takes no input, so a binding pointing at one is a wiring
        /// mistake worth naming rather than silently ignoring.
        /// </summary>
        [Test]
        public void APassiveCannotBeCast()
        {
            var book = Book(new TestAbility("ability.passive", AbilityRole.Passive));

            Assert.That(book.TryBegin(0), Is.EqualTo(CastOutcome.NotCastable));
        }

        [Test]
        public void HasteShortensTheCooldownOfWhatIsCast()
        {
            var book = Book(new TestAbility { Cooldown = 10f });

            book.TryBegin(0, 0.5f);

            Assert.That(book[0].CooldownRemaining, Is.EqualTo(5f).Within(0.001f));
        }

        // --- Channelling ---------------------------------------------------------------------

        [Test]
        public void AChannelResolvesWhenItIsLetGo_AndReportsHowLongItWasHeld()
        {
            var book = Book(new TestAbility { CastCost = CastCost.Channelled, MaxChannelDuration = 3f });
            var held = -1f;

            book.CastResolved += (_, duration) => held = duration;

            book.TryBegin(0);
            book.Tick(1.5f);

            Assert.That(book.IsChannelling, Is.True);
            Assert.That(held, Is.EqualTo(-1f), "a channel must not resolve while it is held");

            Assert.That(book.ReleaseChannel(), Is.True);
            Assert.That(held, Is.EqualTo(1.5f).Within(0.001f));
        }

        /// <summary>The ceiling decides for a player who never lets go.</summary>
        [Test]
        public void AChannelHeldPastItsCeiling_ResolvesAtTheCeiling()
        {
            var book = Book(new TestAbility { CastCost = CastCost.Channelled, MaxChannelDuration = 2f });
            var held = 0f;

            book.CastResolved += (_, duration) => held = duration;

            book.TryBegin(0);
            book.Tick(5f);

            Assert.That(held, Is.EqualTo(2f), "the channel should have been capped, not overrun");
            Assert.That(book.IsCasting, Is.False);
        }

        [Test]
        public void ReleasingWhenNothingIsChannelled_DoesNothing()
        {
            var book = Book(new TestAbility());

            Assert.That(book.ReleaseChannel(), Is.False);
        }

        // --- Interruption --------------------------------------------------------------------

        /// <summary>
        /// The charge is spent at the start, so an interrupted cast has still cost its cooldown. That is
        /// what makes committing to a channel a decision rather than a free option (§5.5.4).
        /// </summary>
        [Test]
        public void AnInterruptedCast_DoesNotResolveAndStillCostsItsCooldown()
        {
            var book = Book(new TestAbility { CastTime = 1f, CastCost = CastCost.Rooted, Cooldown = 9f });
            var resolved = 0;
            var interrupted = 0;

            book.CastResolved += (_, __) => resolved++;
            book.CastInterrupted += _ => interrupted++;

            book.TryBegin(0);
            book.Tick(0.5f);

            Assert.That(book.Interrupt(), Is.True);

            book.Tick(5f);

            Assert.That(resolved, Is.Zero);
            Assert.That(interrupted, Is.EqualTo(1));
            Assert.That(book[0].CooldownRemaining, Is.GreaterThan(0f));
        }

        [Test]
        public void InterruptingNothing_IsNotAnError()
        {
            Assert.That(Book(new TestAbility()).Interrupt(), Is.False);
        }

        // --- Cast cost and movement ------------------------------------------------------------

        /// <summary>
        /// The four costs have to be distinguishable while playing, which means three different
        /// multipliers and one of them exactly zero (§5.5.4).
        /// </summary>
        [Test]
        public void EachCastCost_MultipliesMovementDifferently()
        {
            const float Slowed = 0.4f;

            Assert.That(Casting(CastCost.Free).MovementMultiplier(Slowed), Is.EqualTo(1f));
            Assert.That(Casting(CastCost.Slowed).MovementMultiplier(Slowed), Is.EqualTo(Slowed));
            Assert.That(Casting(CastCost.Rooted).MovementMultiplier(Slowed), Is.EqualTo(0f));
            Assert.That(Casting(CastCost.Channelled).MovementMultiplier(Slowed), Is.EqualTo(0f));
        }

        [Test]
        public void NotCasting_CostsNoMovement()
        {
            Assert.That(Book(new TestAbility()).MovementMultiplier(0.4f), Is.EqualTo(1f));
        }

        static AbilityBook Casting(CastCost cost)
        {
            var book = Book(new TestAbility
            {
                CastCost = cost,
                CastTime = cost == CastCost.Channelled ? 0f : 2f,
                MaxChannelDuration = cost == CastCost.Channelled ? 5f : 0f
            });

            book.TryBegin(0);

            return book;
        }

        // --- The passive pulse -----------------------------------------------------------------

        [Test]
        public void ThePassivePulsesOnItsInterval()
        {
            var book = Complete();
            var pulses = 0;

            book.PassivePulse += (_, __) => pulses++;

            book.Tick(0.2f, 0.25f);

            Assert.That(pulses, Is.Zero);

            book.Tick(0.05f, 0.25f);

            Assert.That(pulses, Is.EqualTo(1));
        }

        /// <summary>
        /// The pulse reports how much time it stands for, which is what lets a passive be authored as a
        /// rate — so retuning the interval for frame time does not change what the passive does (§5.5.3).
        /// </summary>
        [Test]
        public void APulseReportsTheTimeItCovers()
        {
            var book = Complete();
            var covered = 0f;

            book.PassivePulse += (_, interval) => covered = interval;

            book.Tick(1f, 0.25f);

            Assert.That(covered, Is.EqualTo(0.25f));
        }

        /// <summary>
        /// A frame that took a second must not be paid back as a second of passive damage at once: the
        /// catch-up is bounded and the rest of the owed time is dropped.
        /// </summary>
        [Test]
        public void AHitchDoesNotPayBackAsABurstOfPulses()
        {
            var book = Complete();
            var pulses = 0;

            book.PassivePulse += (_, __) => pulses++;

            book.Tick(5f, 0.1f);

            Assert.That(pulses, Is.EqualTo(AbilityBook.MaxPulsesPerTick));
        }

        [Test]
        public void WithNoPassiveAuthored_NothingPulses()
        {
            var book = Book(new TestAbility());
            var pulses = 0;

            book.PassivePulse += (_, __) => pulses++;

            book.Tick(10f, 0.1f);

            Assert.That(pulses, Is.Zero);
        }

        /// <summary>
        /// A passive is not a cast and does not share the one-at-a-time rule: a champion mid-channel is
        /// still taking the floor out from under everything standing on it.
        /// </summary>
        [Test]
        public void APassiveKeepsPulsingWhileSomethingElseIsBeingCast()
        {
            var book = Book(
                new TestAbility("ability.aura", AbilityRole.Passive) { Cooldown = 0f },
                new TestAbility("ability.burst") { CastTime = 1f, CastCost = CastCost.Rooted });

            var pulses = 0;

            book.PassivePulse += (_, __) => pulses++;

            Assert.That(book.TryBegin(1), Is.EqualTo(CastOutcome.Started));

            book.Tick(0.3f, 0.25f);

            Assert.That(book.IsCasting, Is.True);
            Assert.That(pulses, Is.EqualTo(1));
        }

        [Test]
        public void AnIntervalOfZero_StopsThePassiveRatherThanSpinning()
        {
            var book = Complete();
            var pulses = 0;

            book.PassivePulse += (_, __) => pulses++;

            book.Tick(1f, 0f);

            Assert.That(pulses, Is.Zero);
        }

        // --- Time ------------------------------------------------------------------------------

        [Test]
        public void ATickOfNothing_ChangesNothing()
        {
            var book = Book(new TestAbility { Cooldown = 5f });

            book.TryBegin(0);
            book.Tick(0f);
            book.Tick(float.NaN);

            Assert.That(book[0].CooldownRemaining, Is.EqualTo(5f));
        }

        [Test]
        public void ResettingReturnsEverythingAndDropsTheCast()
        {
            var book = Book(new TestAbility { CastTime = 2f, CastCost = CastCost.Rooted, Cooldown = 30f });

            book.TryBegin(0);
            book.Reset();

            Assert.That(book.IsCasting, Is.False);
            Assert.That(book[0].IsReady, Is.True);
        }
    }
}
