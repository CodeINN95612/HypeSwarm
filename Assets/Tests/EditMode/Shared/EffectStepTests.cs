using HypeSwarm.Shared.Abilities;
using HypeSwarm.Shared.Abilities.Steps;
using HypeSwarm.Shared.Combat;
using HypeSwarm.Shared.Stats;
using NUnit.Framework;
using UnityEngine;

namespace HypeSwarm.Shared.Tests
{
    /// <summary>
    /// The authored effect steps, each run against a fake world with no scene and no network.
    /// </summary>
    /// <remarks>
    /// These are the tests that make the phase exit criterion meaningful. "An ability is authored
    /// entirely in the inspector" is only true if the steps do what their fields say, and a step that
    /// silently affects nothing — because nothing selected targets, or because it ran on the wrong
    /// machine — is the failure mode authoring invites.
    ///
    /// <para>Steps are <c>ScriptableObject</c>s created in memory rather than assets on disk, and their
    /// private serialised fields are written the way the inspector writes them. No asset to author, no
    /// import, no dependency on what happens to be in the project.</para>
    /// </remarks>
    [TestFixture]
    public sealed class EffectStepTests
    {
        TestCombatant caster;
        TestWorld world;

        [SetUp]
        public void SetUp()
        {
            caster = new TestCombatant(Faction.Players, Vector3.zero, 500f, 1u);
            world = new TestWorld(caster);
        }

        [TearDown]
        public void TearDown()
        {
            caster = null;
            world = null;
        }

        AbilityContext Context(bool authority = true, float deltaTime = 0f, float channel = 0f)
        {
            return new AbilityContext
            {
                Caster = caster,
                Ability = new TestAbility(),
                Source = ModifierSource.Parse("ability.test"),
                Aim = AbilityAim.FromPoint(Vector3.zero, new Vector3(0f, 0f, 3f), Vector2.up),
                IsAuthority = authority,
                World = world,
                DeltaTime = deltaTime,
                ChannelDuration = channel
            };
        }

        TestCombatant Enemy(float x, float z = 0f)
        {
            var enemy = new TestCombatant(Faction.Enemies, new Vector3(x, 0f, z), 1000f, 2u);

            world.Add(enemy);

            return enemy;
        }

        // --- Select targets --------------------------------------------------------------------

        [Test]
        public void SelectTargets_FillsTheListFromTheAimPoint()
        {
            var near = Enemy(0f, 3f);
            var far = Enemy(0f, 30f);

            var step = AuthoredFields.Step<SelectTargetsStep>("effect.select")
                .With("radius", 4f)
                .With("origin", ShapeOrigin.AimPoint);

            var context = Context();

            step.Execute(context);

            Assert.That(context.Targets, Is.EqualTo(new ICombatant[] { near }));
            Assert.That(context.Targets, Has.No.Member(far));
        }

        [Test]
        public void SelectTargets_CanCentreOnTheCasterInstead()
        {
            var atCaster = Enemy(1f);
            var atAimPoint = Enemy(0f, 3f);

            var step = AuthoredFields.Step<SelectTargetsStep>("effect.aura")
                .With("radius", 2f)
                .With("origin", ShapeOrigin.Caster);

            var context = Context();

            step.Execute(context);

            Assert.That(context.Targets, Contains.Item(atCaster));
            Assert.That(context.Targets, Has.No.Member(atAimPoint));
        }

        /// <summary>
        /// The step-function case, wired end to end: how many are hit is a count derived from a stat, and
        /// the shape it is derived inside never changes (§5.5.5, §5.6.5).
        /// </summary>
        [Test]
        public void SelectTargets_HitsMoreOfThemAsTheStatCrossesThresholds()
        {
            Enemy(1f);
            Enemy(2f);
            Enemy(3f);
            Enemy(4f);

            var step = AuthoredFields.Step<SelectTargetsStep>("effect.fan")
                .With("radius", 20f)
                .With("origin", ShapeOrigin.Caster)
                .With("maxTargets", new StatStep(1, StatId.MoveSpeed, 40f, 4));

            var context = Context();

            step.Execute(context);

            Assert.That(context.Targets, Has.Count.EqualTo(1), "one at no move speed");

            caster.Stats.Add(StatModifier.Flat(StatId.MoveSpeed, 80f, ModifierSource.Parse("item.boots")));

            context.Targets.Clear();
            step.Execute(context);

            Assert.That(context.Targets, Has.Count.EqualTo(3), "two thresholds of move speed is two more");
        }

        [Test]
        public void SelectTargets_CanAddToAnEarlierSelection()
        {
            Enemy(1f);

            var step = AuthoredFields.Step<SelectTargetsStep>("effect.also")
                .With("radius", 20f)
                .With("origin", ShapeOrigin.Caster)
                .With("replacePrevious", false);

            var context = Context();

            context.Targets.Add(new TestCombatant(Faction.Enemies, new Vector3(100f, 0f, 0f)));
            step.Execute(context);

            Assert.That(context.Targets, Has.Count.EqualTo(2));
        }

        /// <summary>
        /// The client selects too, so it can show a hit reaction without waiting a round trip. It just
        /// does not get to decide what the hit does.
        /// </summary>
        [Test]
        public void SelectTargets_RunsOnThePredictingClient()
        {
            var step = AuthoredFields.Step<SelectTargetsStep>("effect.select");

            Assert.That(step.RunsOnPrediction, Is.True);
            Assert.That(step.ProvidesTargets, Is.True);
        }

        // --- Deal damage -----------------------------------------------------------------------

        [Test]
        public void DealDamage_SendsOnePacketPerTargetThroughThePipeline()
        {
            var first = Enemy(1f);
            var second = Enemy(2f);

            var step = AuthoredFields.Step<DealDamageStep>("effect.hit")
                .With("amount", new AbilityScaling(50f));

            var context = Context();

            context.Targets.Add(first);
            context.Targets.Add(second);

            step.Execute(context);

            Assert.That(first.Hits, Has.Count.EqualTo(1));
            Assert.That(second.Hits, Has.Count.EqualTo(1));
            Assert.That(first.Hits[0].Amount, Is.EqualTo(50f));
        }

        /// <summary>
        /// The packet carries where it came from (§5.6.1) and who sent it. A packet without provenance
        /// cannot be retrofitted without revisiting every step that ever made one.
        /// </summary>
        [Test]
        public void DealDamage_SendsAPacketThatKnowsWhereItCameFrom()
        {
            var enemy = Enemy(1f);

            var step = AuthoredFields.Step<DealDamageStep>("effect.hit")
                .With("amount", new AbilityScaling(10f))
                .With("flags", DamageFlags.Ability | DamageFlags.Projectile);

            var context = Context();

            context.Targets.Add(enemy);
            step.Execute(context);

            Assert.That(enemy.Hits[0].Has(DamageFlags.Projectile), Is.True);
            Assert.That(enemy.Hits[0].AttackerNetId, Is.EqualTo(caster.Id));
            Assert.That(enemy.Hits[0].Source, Is.EqualTo(context.Source));
        }

        [Test]
        public void DealDamage_ScalesFromTheCasterStats()
        {
            var enemy = Enemy(1f);

            caster.Stats.Add(StatModifier.Flat(StatId.Damage, 60f, ModifierSource.Parse("item.sword")));

            var step = AuthoredFields.Step<DealDamageStep>("effect.hit")
                .With("amount", new AbilityScaling(10f, StatId.Damage, 0.5f));

            var context = Context();

            context.Targets.Add(enemy);
            step.Execute(context);

            var expected = 10f + 0.5f * caster.Stats.Get(StatId.Damage);

            Assert.That(enemy.Hits[0].Amount, Is.EqualTo(expected).Within(0.001f));
        }

        /// <summary>
        /// A passive authored as damage per second keeps its rate whatever the tick interval is, which is
        /// what lets the interval be retuned for frame time without rebalancing anything (§5.5.3).
        /// </summary>
        [Test]
        public void DealDamage_PerSecond_DoesNotDependOnTheTickInterval()
        {
            var step = AuthoredFields.Step<DealDamageStep>("effect.aura")
                .With("amount", new AbilityScaling(40f))
                .With("perSecond", true);

            // Exact binary fractions, and the pulse count stated rather than divided for: a tenth of a
            // second does not exist in binary, and a test that loses its last iteration to that would
            // fail for a reason that has nothing to do with the thing being tested.
            var fast = Total(step, interval: 0.125f, pulses: 8);
            var slow = Total(step, interval: 0.5f, pulses: 2);

            Assert.That(fast, Is.EqualTo(40f).Within(0.001f));
            Assert.That(slow, Is.EqualTo(40f).Within(0.001f));
        }

        float Total(DealDamageStep step, float interval, int pulses)
        {
            var enemy = Enemy(1f);

            for (var i = 0; i < pulses; i++)
            {
                var context = Context(deltaTime: interval);

                context.Targets.Add(enemy);
                step.Execute(context);
            }

            return enemy.TotalRequested;
        }

        /// <summary>
        /// The channel pays off in proportion to what the player committed, which is the whole reason the
        /// cost is interesting (§5.5.4).
        /// </summary>
        [Test]
        public void DealDamage_ScalesWithHowLongAChannelWasHeld()
        {
            var enemy = Enemy(1f);

            var step = AuthoredFields.Step<DealDamageStep>("effect.release")
                .With("amount", new AbilityScaling(100f))
                .With("channelBonusPerSecond", 0.5f);

            var context = Context(channel: 2f);

            context.Targets.Add(enemy);
            step.Execute(context);

            Assert.That(enemy.Hits[0].Amount, Is.EqualTo(200f).Within(0.001f));
        }

        /// <summary>
        /// <b>The client must not apply its own damage.</b> The step runs on the host only, and the
        /// runner is what honours that — this pins the declaration the runner reads.
        /// </summary>
        [Test]
        public void DealDamage_DoesNotRunOnAPredictingClient()
        {
            var step = AuthoredFields.Step<DealDamageStep>("effect.hit");

            Assert.That(step.RunsOnAuthority, Is.True);
            Assert.That(step.RunsOnPrediction, Is.False);
            Assert.That(step.NeedsTargets, Is.True);
        }

        [Test]
        public void DealDamage_WithNothingSelected_DoesNothing()
        {
            var step = AuthoredFields.Step<DealDamageStep>("effect.hit")
                .With("amount", new AbilityScaling(50f));

            Assert.DoesNotThrow(() => step.Execute(Context()));
        }

        // --- Shields, heals and modifiers --------------------------------------------------------

        /// <summary>
        /// The §5.6.5 promise that every stat is relevant to someone: damage reduction is this
        /// champion's offensive scaling, with no special case in the stat layer.
        /// </summary>
        [Test]
        public void ApplyShield_ScalesFromADefensiveStatOntoTheCaster()
        {
            caster.Stats.Add(StatModifier.Flat(StatId.DamageReduction, 60f, ModifierSource.Parse("item.plate")));

            var step = AuthoredFields.Step<ApplyShieldStep>("effect.ward")
                .With("onCaster", true)
                .With("amount", new AbilityScaling(20f, StatId.DamageReduction, 1f))
                .With("duration", 4f);

            step.Execute(Context());

            Assert.That(caster.LastShieldAmount, Is.EqualTo(20f + caster.Stats.Get(StatId.DamageReduction)));
            Assert.That(caster.LastShieldDuration, Is.EqualTo(4f));
        }

        [Test]
        public void ApplyShield_OnTheCaster_NeedsNoTargetingStep()
        {
            var onCaster = AuthoredFields.Step<ApplyShieldStep>("effect.self").With("onCaster", true);
            var onTargets = AuthoredFields.Step<ApplyShieldStep>("effect.others").With("onCaster", false);

            Assert.That(onCaster.NeedsTargets, Is.False);
            Assert.That(onTargets.NeedsTargets, Is.True);
        }

        [Test]
        public void Heal_RestoresTheSelectedTargets()
        {
            var ally = new TestCombatant(Faction.Players, new Vector3(1f, 0f, 0f), 500f, 3u);

            ally.TakeDamage(new DamagePacket(100f, ModifierSource.Parse("test.hit"), DamageFlags.Ability));

            var step = AuthoredFields.Step<HealStep>("effect.mend")
                .With("onCaster", false)
                .With("amount", new AbilityScaling(40f));

            var context = Context();

            context.Targets.Add(ally);
            step.Execute(context);

            Assert.That(ally.Heals, Is.EqualTo(new[] { 40f }));
            Assert.That(ally.Current, Is.EqualTo(440f));
        }

        /// <summary>
        /// A slow is negative move speed and nothing more. One step covers every buff and debuff, which
        /// is what keeps a second opinion about where numbers come from out of the game (§5.6.5).
        /// </summary>
        [Test]
        public void ApplyStatModifier_SlowsWithNegativeMoveSpeed()
        {
            var enemy = Enemy(1f);

            var step = AuthoredFields.Step<ApplyStatModifierStep>("effect.chill")
                .With("onCaster", false)
                .With("stat", StatId.MoveSpeed)
                .With("operation", ModifierOperation.FlatAdd)
                .With("magnitude", new AbilityScaling(-50f))
                .With("duration", 2f);

            var context = Context();

            context.Targets.Add(enemy);
            step.Execute(context);

            Assert.That(enemy.Applied, Has.Count.EqualTo(1));
            Assert.That(enemy.Applied[0].Value, Is.EqualTo(-50f));
            Assert.That(enemy.Stats.Effect(StatId.MoveSpeed), Is.LessThan(0f));
            Assert.That(enemy.Stats.Effect(StatId.MoveSpeed), Is.GreaterThan(-1f),
                "a slow must not be able to stop something dead");
        }

        [Test]
        public void ApplyStatModifier_CarriesTheAbilityAsItsSource_SoItComesOffAsAUnit()
        {
            var enemy = Enemy(1f);

            var step = AuthoredFields.Step<ApplyStatModifierStep>("effect.chill")
                .With("onCaster", false)
                .With("magnitude", new AbilityScaling(-30f));

            var context = Context();

            context.Targets.Add(enemy);
            step.Execute(context);

            Assert.That(enemy.Applied[0].Source, Is.EqualTo(context.Source));
            Assert.That(enemy.Stats.RemoveSource(context.Source), Is.EqualTo(1));
            Assert.That(enemy.Stats.Get(StatId.MoveSpeed), Is.EqualTo(enemy.Stats.GetBase(StatId.MoveSpeed)));
        }

        [Test]
        public void ApplyStatModifier_OfZero_AppliesNothing()
        {
            var enemy = Enemy(1f);

            var step = AuthoredFields.Step<ApplyStatModifierStep>("effect.nothing")
                .With("onCaster", false)
                .With("magnitude", new AbilityScaling(0f));

            var context = Context();

            context.Targets.Add(enemy);
            step.Execute(context);

            Assert.That(enemy.Applied, Is.Empty);
        }

        // --- Dash ------------------------------------------------------------------------------

        sealed class Dashes : IMobility
        {
            public int Calls;

            public Vector2 Direction;

            public bool TryDash(Vector2 direction)
            {
                Calls++;
                Direction = direction;

                return true;
            }
        }

        /// <summary>
        /// Mobility is the one thing that must happen before the host has said anything, because a dash
        /// that arrives a round trip later reads as a dropped input (§5.5.1).
        /// </summary>
        [Test]
        public void Dash_RunsOnThePredictingClient()
        {
            var step = AuthoredFields.Step<DashStep>("effect.dash");

            Assert.That(step.RunsOnPrediction, Is.True);
        }

        /// <summary>
        /// Zero means "your own default direction", which the motor resolves as travel then facing. A
        /// dash that followed the cursor could only ever go toward what you are shooting.
        /// </summary>
        [Test]
        public void Dash_LeavesTheDirectionToTheMotorByDefault()
        {
            var mobility = new Dashes();
            var step = AuthoredFields.Step<DashStep>("effect.dash").With("towardAim", false);

            var context = Context();

            context.Mobility = mobility;
            step.Execute(context);

            Assert.That(mobility.Calls, Is.EqualTo(1));
            Assert.That(mobility.Direction, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void Dash_CanBeAuthoredToFollowAim()
        {
            var mobility = new Dashes();
            var step = AuthoredFields.Step<DashStep>("effect.leap").With("towardAim", true);

            var context = Context();

            context.Mobility = mobility;
            step.Execute(context);

            Assert.That(mobility.Direction, Is.EqualTo(Vector2.up));
        }

        /// <summary>
        /// On the host and on the other four clients there is no motor to move, and there must not be:
        /// movement is client-authoritative, so a dash step elsewhere would fight the replicated
        /// transform or teleport a champion this machine does not own (§10).
        /// </summary>
        [Test]
        public void Dash_WithNoMotor_DoesNothing()
        {
            var step = AuthoredFields.Step<DashStep>("effect.dash");

            Assert.DoesNotThrow(() => step.Execute(Context()));
        }
    }
}
