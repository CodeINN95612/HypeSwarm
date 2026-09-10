using System.Collections.Generic;
using HypeSwarm.Shared.Abilities;
using HypeSwarm.Shared.Combat;
using NUnit.Framework;
using UnityEngine;

namespace HypeSwarm.Shared.Tests
{
    /// <summary>
    /// What counts as a hit: the shape, the side, and the cap — plus the scan that answers it.
    /// </summary>
    /// <remarks>
    /// <b>This is the contract the spatial hash has to keep at Phase 2 step 7.</b> The implementation
    /// behind <see cref="IAbilityWorld"/> is going to be replaced, and these tests are how the
    /// replacement is shown to select the same things: the rules live on the query, not in the container.
    ///
    /// <para>Shapes are planar on purpose (§5.5.1) — a cone must not miss an enemy standing on a step —
    /// so height is asserted to be ignored rather than left to chance.</para>
    /// </remarks>
    [TestFixture]
    public sealed class TargetQueryTests
    {
        static TestCombatant Enemy(float x, float z = 0f, float y = 0f)
        {
            return new TestCombatant(Faction.Enemies, new Vector3(x, y, z));
        }

        static TestCombatant Ally(float x, float z = 0f)
        {
            return new TestCombatant(Faction.Players, new Vector3(x, 0f, z));
        }

        static List<ICombatant> Find(TargetQuery query, params ICombatant[] candidates)
        {
            var world = new TestWorld(candidates);
            var found = new List<ICombatant>();

            world.FindTargets(query, found);

            return found;
        }

        static TargetQuery Circle(float radius, TargetFaction wanted = TargetFaction.Enemies, ICombatant caster = null, int max = 0)
        {
            return new TargetQuery(Vector3.zero, radius, Faction.Players, wanted, default,
                TargetQuery.FullCircleDegrees, caster, max);
        }

        // --- The shape ------------------------------------------------------------------------

        [Test]
        public void WhatIsInsideTheRadiusIsHit_AndWhatIsOutsideIsNot()
        {
            var near = Enemy(3f);
            var far = Enemy(9f);

            Assert.That(Find(Circle(5f), near, far), Is.EqualTo(new[] { near }));
        }

        /// <summary>
        /// Combat is planar even though the game is 3D. Measuring height would make a cast miss an enemy
        /// standing on a step, which reads as the ability being broken.
        /// </summary>
        [Test]
        public void HeightIsIgnored()
        {
            var raised = Enemy(2f, 0f, y: 40f);

            Assert.That(Find(Circle(5f), raised), Is.EqualTo(new[] { raised }));
        }

        [Test]
        public void AConeHitsWhatIsInFrontAndNotWhatIsBehind()
        {
            var ahead = Enemy(0f, 4f);
            var behind = Enemy(0f, -4f);

            var query = new TargetQuery(
                Vector3.zero, 10f, Faction.Players, TargetFaction.Enemies, Vector2.up, 90f);

            Assert.That(Find(query, ahead, behind), Is.EqualTo(new[] { ahead }));
        }

        [Test]
        public void AConeMeasuresItsTotalAngle_NotItsHalfAngle()
        {
            // 40 degrees off the axis: inside a 90-degree cone, outside a 60-degree one.
            var offAxis = Enemy(Mathf.Sin(40f * Mathf.Deg2Rad) * 5f, Mathf.Cos(40f * Mathf.Deg2Rad) * 5f);

            var wide = new TargetQuery(Vector3.zero, 10f, Faction.Players, TargetFaction.Enemies, Vector2.up, 90f);
            var narrow = new TargetQuery(Vector3.zero, 10f, Faction.Players, TargetFaction.Enemies, Vector2.up, 60f);

            Assert.That(wide.Matches(offAxis), Is.True);
            Assert.That(narrow.Matches(offAxis), Is.False);
        }

        /// <summary>Every cone hits what is standing on top of the caster, whatever the angle.</summary>
        [Test]
        public void AConeHitsWhatIsOnTopOfIt()
        {
            var touching = Enemy(0f, 0f);

            var query = new TargetQuery(
                Vector3.zero, 10f, Faction.Players, TargetFaction.Enemies, Vector2.up, 30f);

            Assert.That(query.Matches(touching), Is.True);
        }

        // --- The side -------------------------------------------------------------------------

        [Test]
        public void AnEnemyQueryDoesNotFindAllies()
        {
            var enemy = Enemy(1f);
            var ally = Ally(1f);

            Assert.That(Find(Circle(5f), enemy, ally), Is.EqualTo(new[] { enemy }));
        }

        [Test]
        public void AnAllyQueryFindsTheCasterToo()
        {
            var caster = Ally(0f);
            var ally = Ally(2f);
            var enemy = Enemy(2f);

            var found = Find(Circle(5f, TargetFaction.Allies, caster), caster, ally, enemy);

            Assert.That(found, Contains.Item(caster));
            Assert.That(found, Contains.Item(ally));
            Assert.That(found, Has.No.Member(enemy));
        }

        [Test]
        public void ACasterQueryFindsNobodyElse()
        {
            var caster = Ally(0f);
            var ally = Ally(1f);

            Assert.That(Find(Circle(5f, TargetFaction.Caster, caster), caster, ally), Is.EqualTo(new[] { caster }));
        }

        /// <summary>
        /// A self-shield needs no shape. Without this, authoring one would mean inventing a radius that
        /// nothing reads and that the next person to look at the asset has to explain.
        /// </summary>
        [Test]
        public void ACasterQueryNeedsNoRadius()
        {
            var caster = Ally(0f);

            Assert.That(Circle(0f, TargetFaction.Caster, caster).Matches(caster), Is.True);
        }

        /// <summary>
        /// Neutral is hostile to nothing and nothing is hostile to it: a destructible must not eat a
        /// cleave meant for the horde.
        /// </summary>
        [Test]
        public void NeutralsAreNotHitByAnything()
        {
            var neutral = new TestCombatant(Faction.Neutral, new Vector3(1f, 0f, 0f));

            Assert.That(Find(Circle(5f), neutral), Is.Empty);
            Assert.That(Find(Circle(5f, TargetFaction.Allies), neutral), Is.Empty);
        }

        [Test]
        public void TheDeadAreNotHit()
        {
            var corpse = Enemy(1f);

            corpse.Kill();

            Assert.That(Find(Circle(5f), corpse), Is.Empty);
        }

        // --- The cap --------------------------------------------------------------------------

        /// <summary>
        /// Nearest first, and not "whichever the container listed first": a capped ability that picked
        /// arbitrarily would hit different things on two machines and feel random to the player.
        /// </summary>
        [Test]
        public void ACappedQueryTakesTheNearestFirst()
        {
            var far = Enemy(8f);
            var near = Enemy(1f);
            var middle = Enemy(4f);

            var found = Find(Circle(20f, TargetFaction.Enemies, null, max: 2), far, near, middle);

            Assert.That(found, Is.EqualTo(new ICombatant[] { near, middle }));
        }

        [Test]
        public void AnUncappedQueryTakesEverythingInRange()
        {
            var found = Find(Circle(20f), Enemy(1f), Enemy(2f), Enemy(3f));

            Assert.That(found, Has.Count.EqualTo(3));
        }

        [Test]
        public void ACapLargerThanWhatIsThere_IsNotAnError()
        {
            var found = Find(Circle(20f, TargetFaction.Enemies, null, max: 10), Enemy(1f));

            Assert.That(found, Has.Count.EqualTo(1));
        }

        // --- The container --------------------------------------------------------------------

        [Test]
        public void FindingAppendsRatherThanReplacing_SoOneCastCanSelectTwice()
        {
            var world = new TestWorld(Enemy(1f), Enemy(2f));
            var found = new List<ICombatant> { Enemy(50f) };

            world.FindTargets(Circle(20f), found);

            Assert.That(found, Has.Count.EqualTo(3));
        }

        [Test]
        public void RegisteringTheSameCombatantTwice_DoesNotHitItTwice()
        {
            var world = new CombatantWorld();
            var enemy = Enemy(1f);

            world.Register(enemy);
            world.Register(enemy);

            var found = new List<ICombatant>();

            world.FindTargets(Circle(20f), found);

            Assert.That(found, Has.Count.EqualTo(1));
        }

        [Test]
        public void UnregisteringRemovesItFromEverySearch()
        {
            var world = new CombatantWorld();
            var enemy = Enemy(1f);

            world.Register(enemy);
            world.Unregister(enemy);

            var found = new List<ICombatant>();

            Assert.That(world.FindTargets(Circle(20f), found), Is.Zero);
            Assert.That(world.Count, Is.Zero);
        }
    }
}
