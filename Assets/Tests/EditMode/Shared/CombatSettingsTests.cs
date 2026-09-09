using System.Collections.Generic;
using System.IO;
using HypeSwarm.Shared.Combat;
using HypeSwarm.Shared.Tuning;
using NUnit.Framework;
using UnityEngine;

namespace HypeSwarm.Shared.Tests
{
    /// <summary>
    /// The combat numbers that are not stats, and the config keys the shipped file has to define.
    /// </summary>
    /// <remarks>
    /// No test here asserts what the delay or the revive share <i>is</i>. What is checked is that the
    /// file defines them at all, because the mistake this design invites is adding a number, forgetting
    /// the config line, and shipping a game where half the balance lives in a file and half in a
    /// constant.
    /// </remarks>
    [TestFixture]
    public sealed class CombatSettingsTests
    {
        [Test]
        public void TuningOverridesTheCompiledDefaults()
        {
            var config = new TuningConfig(new DictionaryTuningSource("test", new Dictionary<string, string>
            {
                { CombatSettings.RegenDelayKey, "12.5" },
                { CombatSettings.ReviveHealthFractionKey, "0.25" }
            }));

            var settings = CombatSettings.FromTuning(config);

            Assert.That(settings.RegenDelay, Is.EqualTo(12.5f));
            Assert.That(settings.ReviveHealthFraction, Is.EqualTo(0.25f));
        }

        [Test]
        public void AMissingKey_KeepsItsDefaultAndIsReported()
        {
            var config = new TuningConfig(new DictionaryTuningSource("test", new Dictionary<string, string>()));

            var settings = CombatSettings.FromTuning(config);

            Assert.That(settings.RegenDelay, Is.EqualTo(CombatSettings.Default.RegenDelay));
            Assert.That(config.MissingKeys, Contains.Item(CombatSettings.RegenDelayKey));
        }

        [Test]
        public void TheCompiledDefaults_HaveNothingWrongWithThem()
        {
            Assert.That(CombatSettings.Default.Validate(), Is.Null);
        }

        [Test]
        public void ValidationCatchesANegativeRegenDelay()
        {
            Assert.That(new CombatSettings(-1f, 0.5f).Validate(), Is.Not.Null);
        }

        /// <summary>
        /// A revive fraction outside its range is either a revive that comes back dead or one that
        /// comes back with more health than the champion has.
        /// </summary>
        [Test]
        public void ValidationCatchesAReviveShareOutsideItsRange()
        {
            Assert.That(new CombatSettings(5f, 0f).Validate(), Is.Not.Null);
            Assert.That(new CombatSettings(5f, 1.5f).Validate(), Is.Not.Null);
        }

        [Test]
        public void TheShippedTuningFile_DefinesEveryCombatKey()
        {
            var directory = Path.Combine(Application.streamingAssetsPath, TuningLoader.DirectoryName);

            Assert.That(Directory.Exists(directory), $"No shipped tuning directory at {directory}");

            var config = TuningLoader.Load(new[] { directory }, out var errors);

            Assert.That(errors, Is.Empty);
            Assert.That(config.AllKeys, Contains.Item(CombatSettings.RegenDelayKey));
            Assert.That(config.AllKeys, Contains.Item(CombatSettings.ReviveHealthFractionKey));
        }

        [Test]
        public void TheShippedTuningFile_ValidatesClean()
        {
            var directory = Path.Combine(Application.streamingAssetsPath, TuningLoader.DirectoryName);
            var config = TuningLoader.Load(new[] { directory }, out _);

            Assert.That(CombatSettings.FromTuning(config).Validate(), Is.Null);
        }
    }
}
