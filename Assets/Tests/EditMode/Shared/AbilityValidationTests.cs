using System.Collections.Generic;
using System.Linq;
using HypeSwarm.Shared.Abilities;
using HypeSwarm.Shared.Content;
using NUnit.Framework;

namespace HypeSwarm.Shared.Tests
{
    /// <summary>
    /// The design rules the spec states as prose, enforced as checks.
    /// </summary>
    /// <remarks>
    /// The whole value of authoring content in the inspector is that somebody who has not read the spec
    /// can do it — which means the rules that matter most are the ones they will break by accident. These
    /// tests are the manifest: every rule the spec names as a trap, and every structural guarantee the
    /// five-slot layout makes, has a case here.
    /// </remarks>
    [TestFixture]
    public sealed class AbilityValidationTests
    {
        static AbilitySettings Settings => AbilitySettings.Default;

        static IReadOnlyList<ValidationIssue> Check(IAbilityDefinition ability)
        {
            return AbilityValidation.Validate(ability, Settings);
        }

        static bool HasError(IReadOnlyList<ValidationIssue> issues)
        {
            return issues.Any(issue => issue.Severity == ValidationSeverity.Error);
        }

        static TestAbility Valid(AbilityRole role = AbilityRole.Primary)
        {
            var ability = new TestAbility("ability.valid", role)
            {
                Cooldown = 2f,
                Charges = 1,
                Targeting = TargetingMode.AimPoint,
                Range = 8f
            };

            ability.StepList.Add(new RecordingStep());

            return ability;
        }

        [Test]
        public void AWellFormedAbility_HasNothingWrongWithIt()
        {
            Assert.That(Check(Valid()), Is.Empty);
        }

        // --- Cast cost ------------------------------------------------------------------------

        /// <summary>
        /// Mobility is always free-cast (§5.5.4). A mobility slot that rooted you could not serve its
        /// escape function, which is the one thing it exists for.
        /// </summary>
        [Test]
        public void AMobilityAbilityThatIsNotFreeCast_IsAnError()
        {
            var ability = Valid(AbilityRole.Mobility);

            ability.CastCost = CastCost.Rooted;
            ability.CastTime = 0.2f;

            Assert.That(HasError(Check(ability)), Is.True);
        }

        /// <summary>
        /// On the trap list: in a dense horde a long root is a death sentence rather than a decision.
        /// </summary>
        [Test]
        public void ARootedCastLongerThanTheCeiling_IsAnError()
        {
            var ability = Valid();

            ability.CastCost = CastCost.Rooted;
            ability.CastTime = Settings.RootedCastCeiling + 1f;

            Assert.That(HasError(Check(ability)), Is.True);
        }

        [Test]
        public void AShortRootedCast_IsFine()
        {
            var ability = Valid();

            ability.CastCost = CastCost.Rooted;
            ability.CastTime = Settings.RootedCastCeiling;

            Assert.That(Check(ability), Is.Empty);
        }

        [Test]
        public void AChannelWithNoCeiling_WouldNeverResolveAndIsAnError()
        {
            var ability = Valid();

            ability.CastCost = CastCost.Channelled;
            ability.MaxChannelDuration = 0f;

            Assert.That(HasError(Check(ability)), Is.True);
        }

        [Test]
        public void AChannelDurationOnSomethingThatDoesNotChannel_IsWorthAWarning()
        {
            var ability = Valid();

            ability.MaxChannelDuration = 3f;

            Assert.That(Check(ability).Any(issue => issue.Severity == ValidationSeverity.Warning), Is.True);
        }

        /// <summary>A passive is never cast: it pulses on a tick and takes no input (§5.5.3).</summary>
        [Test]
        public void APassiveWithACastCost_IsAnError()
        {
            var ability = Valid(AbilityRole.Passive);

            ability.CastCost = CastCost.Rooted;
            ability.CastTime = 0.3f;

            Assert.That(HasError(Check(ability)), Is.True);
        }

        [Test]
        public void APassiveWithACooldown_IsWorthAWarning_BecauseNothingReadsIt()
        {
            var ability = Valid(AbilityRole.Passive);

            ability.Cooldown = 5f;

            Assert.That(Check(ability).Any(issue => issue.Severity == ValidationSeverity.Warning), Is.True);
        }

        // --- Cooldowns and charges --------------------------------------------------------------

        [Test]
        public void AnAbilityWithNoCharges_CouldNeverBeCast()
        {
            var ability = Valid();

            ability.Charges = 0;

            Assert.That(HasError(Check(ability)), Is.True);
        }

        [Test]
        public void ANegativeCooldown_IsAnError()
        {
            var ability = Valid();

            ability.Cooldown = -1f;

            Assert.That(HasError(Check(ability)), Is.True);
        }

        /// <summary>
        /// Several charges and no lockout means one frame of input can spend two, which players read as
        /// the game eating an input (§5.5.3).
        /// </summary>
        [Test]
        public void MultipleChargesWithNoLockout_IsWorthAWarning()
        {
            var ability = Valid();

            ability.Charges = 3;
            ability.ChargeLockout = 0f;

            Assert.That(Check(ability).Any(issue => issue.Severity == ValidationSeverity.Warning), Is.True);
        }

        // --- Aiming ----------------------------------------------------------------------------

        [Test]
        public void AimingAtAPointWithNoRange_WouldLandOnTheCasterAndIsAnError()
        {
            var ability = Valid();

            ability.Range = 0f;

            Assert.That(HasError(Check(ability)), Is.True);
        }

        // --- Steps -----------------------------------------------------------------------------

        [Test]
        public void AnAbilityWithNoSteps_IsWorthAWarning()
        {
            var ability = Valid();

            ability.StepList.Clear();

            Assert.That(Check(ability).Any(issue => issue.Severity == ValidationSeverity.Warning), Is.True);
        }

        /// <summary>
        /// The commonest authoring mistake by a distance, and it fails silently: the ability casts, the
        /// cooldown starts, and nothing takes a point of damage.
        /// </summary>
        [Test]
        public void ADamageStepWithNothingSelectingTargets_IsAnError()
        {
            var ability = Valid();

            ability.StepList.Clear();
            ability.StepList.Add(new RecordingStep { NeedsTargets = true });

            Assert.That(HasError(Check(ability)), Is.True);
        }

        [Test]
        public void ATargetingStepInFrontOfIt_MakesItFine()
        {
            var ability = Valid();

            ability.StepList.Clear();
            ability.StepList.Add(new RecordingStep { ProvidesTargets = true });
            ability.StepList.Add(new RecordingStep { NeedsTargets = true });

            Assert.That(Check(ability), Is.Empty);
        }

        /// <summary>Order matters: selecting after damaging is the same mistake in a different coat.</summary>
        [Test]
        public void ATargetingStepAfterTheDamage_IsStillAnError()
        {
            var ability = Valid();

            ability.StepList.Clear();
            ability.StepList.Add(new RecordingStep { NeedsTargets = true });
            ability.StepList.Add(new RecordingStep { ProvidesTargets = true });

            Assert.That(HasError(Check(ability)), Is.True);
        }

        [Test]
        public void AnEmptyStepSlot_IsAnError()
        {
            var ability = Valid();

            ability.StepList.Clear();
            ability.StepList.Add(null);

            Assert.That(HasError(Check(ability)), Is.True);
        }

        [Test]
        public void AStepThatRunsNowhere_IsWorthAWarning()
        {
            var ability = Valid();

            ability.StepList.Clear();
            ability.StepList.Add(new RecordingStep(onAuthority: false, onPrediction: false));

            Assert.That(Check(ability).Any(issue => issue.Severity == ValidationSeverity.Warning), Is.True);
        }

        /// <summary>
        /// Start steps are checked the same way, because the mistake is the same one and it fails just as
        /// silently.
        /// </summary>
        [Test]
        public void AStartStepThatNeedsTargetsWithNothingSelecting_IsAnError()
        {
            var ability = Valid();

            ability.StartStepList.Add(new RecordingStep { NeedsTargets = true });

            Assert.That(HasError(Check(ability)), Is.True);
        }

        /// <summary>
        /// Almost every ability has no start steps at all, so an empty list there is the normal case and
        /// must not be reported — a warning nobody can act on is a warning everybody learns to ignore.
        /// </summary>
        [Test]
        public void AnEmptyStartStepList_IsNotWorthMentioning()
        {
            Assert.That(Check(Valid()), Is.Empty);
        }

        [Test]
        public void AnAbilityWithOnlyStartSteps_IsNotReportedAsDoingNothing()
        {
            var ability = Valid();

            ability.StepList.Clear();
            ability.StartStepList.Add(new RecordingStep());

            Assert.That(Check(ability), Is.Empty);
        }

        // --- Champions --------------------------------------------------------------------------

        [Test]
        public void ACompleteChampion_HasNothingWrongWithIt()
        {
            Assert.That(AbilityValidation.Validate(TestChampion.Complete()), Is.Empty);
        }

        /// <summary>
        /// On the trap list: with no basic attacks, every bit of spacing a champion has comes from
        /// movement, so a champion without mobility has no way to create distance.
        /// </summary>
        [Test]
        public void AChampionWithNoMobilityAbility_IsAnError()
        {
            var champion = TestChampion.Complete();

            champion.AbilityList.RemoveAll(ability => ability.Role == AbilityRole.Mobility);

            var issues = AbilityValidation.Validate(champion);

            Assert.That(HasError(issues), Is.True);
            Assert.That(issues.Any(issue => issue.Message.Contains("Mobility")), Is.True,
                "the message has to name the role, or the author has to guess which one is missing");
        }

        [Test]
        public void EveryMissingRole_IsReportedSeparately()
        {
            var champion = new TestChampion("champion.thin", new TestAbility("ability.one"));

            var issues = AbilityValidation.Validate(champion);

            foreach (var role in AbilityRoles.All)
            {
                if (role == AbilityRole.Primary)
                {
                    continue;
                }

                Assert.That(issues.Any(issue => issue.Message.Contains(role.ToString())), Is.True,
                    $"nothing reported the missing {role} ability");
            }
        }

        /// <summary>
        /// Augments target the role generically, so two abilities in one role means one of them is
        /// unreachable by every augment that names it (§8.3).
        /// </summary>
        [Test]
        public void TwoAbilitiesInOneRole_IsAnError()
        {
            var champion = TestChampion.Complete();

            champion.AbilityList.Add(new TestAbility("ability.second_ultimate", AbilityRole.Ultimate));

            Assert.That(HasError(AbilityValidation.Validate(champion)), Is.True);
        }

        [Test]
        public void AnEmptyChampionSlot_IsAnError()
        {
            var champion = TestChampion.Complete();

            champion.AbilityList.Add(null);

            Assert.That(HasError(AbilityValidation.Validate(champion)), Is.True);
        }

        [Test]
        public void AChampionWithNoAbilitiesAtAll_IsAnError()
        {
            Assert.That(HasError(AbilityValidation.Validate(new TestChampion("champion.empty"))), Is.True);
        }
    }
}
