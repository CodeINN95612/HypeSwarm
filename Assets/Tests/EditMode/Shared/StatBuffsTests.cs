using System.Collections.Generic;
using HypeSwarm.Shared.Stats;
using NUnit.Framework;

namespace HypeSwarm.Shared.Tests
{
    /// <summary>
    /// Temporary modifiers. This is a lifecycle, and the working rules single lifecycles out: at
    /// horde density a buff that fails to come off is a leak, and a leak is invisible until it is a
    /// crash.
    /// </summary>
    [TestFixture]
    public sealed class StatBuffsTests
    {
        static readonly ModifierSource Rage = ModifierSource.Parse("effect.rage");
        static readonly ModifierSource Chill = ModifierSource.Parse("effect.chill");

        static StatModifier Damage(float amount, ModifierSource source)
        {
            return StatModifier.Flat(StatId.Damage, amount, source);
        }

        [Test]
        public void ABuffApplies_AndComesOffWhenItRunsOut()
        {
            var sheet = new StatSheet();
            var buffs = new StatBuffs().Bind(sheet);
            var before = sheet.Get(StatId.Damage);

            buffs.Apply(Rage, 5f, Damage(25f, Rage));

            Assert.That(sheet.Get(StatId.Damage), Is.EqualTo(before + 25f).Within(1e-4f));

            buffs.Tick(4.9f);

            Assert.That(buffs.Count, Is.EqualTo(1), "still running just before it ends");

            buffs.Tick(0.2f);

            Assert.That(buffs.Count, Is.EqualTo(0));
            Assert.That(sheet.Get(StatId.Damage), Is.EqualTo(before).Within(1e-4f));
            Assert.That(sheet.Modifiers, Is.Empty, "nothing left behind on the sheet");
        }

        /// <summary>
        /// A buff re-applied by an aura ticking every second must not leave a copy of itself behind
        /// each time. Stacking is a source per stack, deliberately.
        /// </summary>
        [Test]
        public void ReapplyingTheSameSource_RefreshesInsteadOfStacking()
        {
            var sheet = new StatSheet();
            var buffs = new StatBuffs().Bind(sheet);
            var before = sheet.Get(StatId.Damage);

            buffs.Apply(Rage, 5f, Damage(25f, Rage));
            buffs.Tick(4f);
            buffs.Apply(Rage, 5f, Damage(25f, Rage));

            Assert.That(buffs.Count, Is.EqualTo(1));
            Assert.That(sheet.Get(StatId.Damage), Is.EqualTo(before + 25f).Within(1e-4f));
            Assert.That(buffs.RemainingOn(Rage), Is.EqualTo(5f).Within(1e-4f));
        }

        [Test]
        public void TwoInstancesOfOneEffect_StackAndExpireSeparately()
        {
            var sheet = new StatSheet();
            var buffs = new StatBuffs().Bind(sheet);
            var before = sheet.Get(StatId.Damage);

            var first = new ModifierSource(Rage.Content, 0);
            var second = new ModifierSource(Rage.Content, 1);

            buffs.Apply(first, 2f, Damage(10f, first));
            buffs.Apply(second, 6f, Damage(10f, second));

            Assert.That(sheet.Get(StatId.Damage), Is.EqualTo(before + 20f).Within(1e-4f));

            buffs.Tick(3f);

            Assert.That(sheet.Get(StatId.Damage), Is.EqualTo(before + 10f).Within(1e-4f));

            buffs.Tick(4f);

            Assert.That(sheet.Get(StatId.Damage), Is.EqualTo(before).Within(1e-4f));
        }

        [Test]
        public void ABuffCancelledEarly_ComesOffImmediately()
        {
            var sheet = new StatSheet();
            var buffs = new StatBuffs().Bind(sheet);
            var before = sheet.Get(StatId.Damage);

            buffs.Apply(Rage, 60f, Damage(25f, Rage));

            Assert.That(buffs.Remove(Rage), Is.True);
            Assert.That(sheet.Get(StatId.Damage), Is.EqualTo(before).Within(1e-4f));
            Assert.That(buffs.Remove(Rage), Is.False, "removing it twice is not an error");
        }

        [Test]
        public void ClearingEverything_EndsEveryBuff()
        {
            var sheet = new StatSheet();
            var buffs = new StatBuffs().Bind(sheet);

            buffs.Apply(Rage, 10f, Damage(25f, Rage));
            buffs.Apply(Chill, 10f, StatModifier.Flat(StatId.MoveSpeed, -30f, Chill));

            buffs.Clear();

            Assert.That(buffs.Count, Is.EqualTo(0));
            Assert.That(sheet.Modifiers, Is.Empty);
        }

        /// <summary>
        /// A buff built from an authored template carries whatever source the template had. Stamping
        /// it here is what stops removal from silently matching nothing.
        /// </summary>
        [Test]
        public void ModifiersAreStamped_WithTheSourceTheyWereAppliedUnder()
        {
            var sheet = new StatSheet();
            var buffs = new StatBuffs().Bind(sheet);

            buffs.Apply(Rage, 5f, Damage(25f, Chill));

            Assert.That(sheet.CountFrom(Rage), Is.EqualTo(1));
            Assert.That(sheet.CountFrom(Chill), Is.EqualTo(0));

            buffs.Remove(Rage);

            Assert.That(sheet.Modifiers, Is.Empty);
        }

        [Test]
        public void ABuffWithNoDurationOrNoModifiers_IsRefused()
        {
            var buffs = new StatBuffs();

            Assert.That(buffs.Apply(Rage, 0f, Damage(1f, Rage)), Is.False);
            Assert.That(buffs.Apply(Rage, -1f, Damage(1f, Rage)), Is.False);
            Assert.That(buffs.Apply(Rage, 5f, (IReadOnlyList<StatModifier>)null), Is.False);
            Assert.That(buffs.Apply(Rage, 5f, new StatModifier[0]), Is.False);
            Assert.That(buffs.Count, Is.EqualTo(0));
        }

        [Test]
        public void TickingByNothing_DoesNothing()
        {
            var buffs = new StatBuffs();

            buffs.Apply(Rage, 5f, Damage(25f, Rage));

            buffs.Tick(0f);
            buffs.Tick(-10f);

            Assert.That(buffs.RemainingOn(Rage), Is.EqualTo(5f).Within(1e-4f));
        }

        /// <summary>
        /// The size of the step must not change when a buff ends, or every buff in the game lasts a
        /// slightly different time on a machine with a different frame rate.
        /// </summary>
        [Test]
        public void ExpiryDoesNotDependOnTheStepSize()
        {
            foreach (var step in new[] { 0.016f, 0.1f, 0.5f })
            {
                var buffs = new StatBuffs();
                var elapsed = 0f;

                buffs.Apply(Rage, 3f, Damage(1f, Rage));

                while (elapsed + step <= 2.9f)
                {
                    buffs.Tick(step);
                    elapsed += step;
                }

                Assert.That(buffs.Count, Is.EqualTo(1), $"ended early at step {step}");

                buffs.Tick(0.5f);

                Assert.That(buffs.Count, Is.EqualTo(0), $"outlived its duration at step {step}");
            }
        }

        [Test]
        public void AnExpiringBuff_AnnouncesItsSource()
        {
            var buffs = new StatBuffs();
            var expired = new List<ModifierSource>();

            buffs.Expired += source => expired.Add(source);

            buffs.Apply(Rage, 1f, Damage(1f, Rage));
            buffs.Apply(Chill, 2f, Damage(1f, Chill));

            buffs.Tick(1.5f);

            Assert.That(expired, Is.EqualTo(new[] { Rage }));

            buffs.Tick(1f);

            Assert.That(expired, Is.EqualTo(new[] { Rage, Chill }));
        }
    }
}
