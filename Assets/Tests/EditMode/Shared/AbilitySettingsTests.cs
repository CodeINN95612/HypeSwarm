using System.Collections.Generic;
using System.IO;
using HypeSwarm.Shared.Abilities;
using HypeSwarm.Shared.Tuning;
using NUnit.Framework;
using UnityEngine;

namespace HypeSwarm.Shared.Tests
{
    /// <summary>
    /// The ability numbers that belong to the system rather than to any one ability, and the config keys
    /// the shipped file has to define.
    /// </summary>
    /// <remarks>
    /// No test here asserts what the pulse interval or the slowed-cast speed <i>is</i>. What is checked is
    /// that the file defines them at all, because the mistake this design invites is adding a number,
    /// forgetting the config line, and shipping a game where half the balance lives in a file and half in
    /// a constant.
    /// </remarks>
    [TestFixture]
    public sealed class AbilitySettingsTests
    {
        [Test]
        public void TuningOverridesTheCompiledDefaults()
        {
            var config = new TuningConfig(new DictionaryTuningSource("test", new Dictionary<string, string>
            {
                { AbilitySettings.PassiveTickIntervalKey, "0.5" },
                { AbilitySettings.SlowedCastSpeedKey, "0.25" },
                { AbilitySettings.RootedCastCeilingKey, "0.8" }
            }));

            var settings = AbilitySettings.FromTuning(config);

            Assert.That(settings.PassiveTickInterval, Is.EqualTo(0.5f));
            Assert.That(settings.SlowedCastSpeed, Is.EqualTo(0.25f));
            Assert.That(settings.RootedCastCeiling, Is.EqualTo(0.8f));
        }

        [Test]
        public void AMissingKey_KeepsItsDefaultAndIsReported()
        {
            var config = new TuningConfig(new DictionaryTuningSource("test", new Dictionary<string, string>()));

            var settings = AbilitySettings.FromTuning(config);

            Assert.That(settings.PassiveTickInterval, Is.EqualTo(AbilitySettings.Default.PassiveTickInterval));
            Assert.That(config.MissingKeys, Contains.Item(AbilitySettings.PassiveTickIntervalKey));
        }

        [Test]
        public void TheCompiledDefaults_HaveNothingWrongWithThem()
        {
            Assert.That(AbilitySettings.Default.Validate(), Is.Null);
        }

        /// <summary>An interval of zero would stop every passive in the game rather than speeding it up.</summary>
        [Test]
        public void ValidationCatchesAPulseIntervalOfZero()
        {
            Assert.That(new AbilitySettings(0f, 0.4f, 0.6f).Validate(), Is.Not.Null);
        }

        /// <summary>
        /// Outside <c>(0, 1)</c> a slowed cast is either a root or a free cast, and the value of the cast
        /// cost axis is that the four are distinguishable while playing (§5.5.4).
        /// </summary>
        [Test]
        public void ValidationCatchesASlowedCastSpeedOutsideItsRange()
        {
            Assert.That(new AbilitySettings(0.25f, 0f, 0.6f).Validate(), Is.Not.Null);
            Assert.That(new AbilitySettings(0.25f, 1f, 0.6f).Validate(), Is.Not.Null);
        }

        [Test]
        public void ValidationCatchesARootedCeilingNoAbilityCouldPass()
        {
            Assert.That(new AbilitySettings(0.25f, 0.4f, 0f).Validate(), Is.Not.Null);
        }

        [Test]
        public void TheShippedTuningFile_DefinesEveryAbilityKey()
        {
            var directory = Path.Combine(Application.streamingAssetsPath, TuningLoader.DirectoryName);

            Assert.That(Directory.Exists(directory), $"No shipped tuning directory at {directory}");

            var config = TuningLoader.Load(new[] { directory }, out var errors);

            Assert.That(errors, Is.Empty);
            Assert.That(config.AllKeys, Contains.Item(AbilitySettings.PassiveTickIntervalKey));
            Assert.That(config.AllKeys, Contains.Item(AbilitySettings.SlowedCastSpeedKey));
            Assert.That(config.AllKeys, Contains.Item(AbilitySettings.RootedCastCeilingKey));
        }

        [Test]
        public void TheShippedTuningFile_ValidatesClean()
        {
            var directory = Path.Combine(Application.streamingAssetsPath, TuningLoader.DirectoryName);
            var config = TuningLoader.Load(new[] { directory }, out _);

            Assert.That(AbilitySettings.FromTuning(config).Validate(), Is.Null);
        }
    }
}
