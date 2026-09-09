using System;
using System.Collections.Generic;
using System.IO;
using HypeSwarm.Shared.Stats;
using HypeSwarm.Shared.Tuning;
using NUnit.Framework;
using UnityEngine;

namespace HypeSwarm.Shared.Tests
{
    /// <summary>
    /// The catalog, and the two things about it that are structural rather than balance: the enum
    /// values a sheet indexes arrays with, and the tuning keys the shipped config has to define.
    /// </summary>
    /// <remarks>
    /// No test here asserts a starting value or a soft cap. Those change weekly, and a test that
    /// pins one gets its expectation updated reflexively until it asserts nothing at all.
    /// </remarks>
    [TestFixture]
    public sealed class StatCatalogTests
    {
        /// <summary>
        /// <see cref="StatSheet"/> indexes three arrays by the raw enum value, so a gap or a
        /// hand-written number out of sequence is an out-of-range exception at runtime rather than a
        /// compile error.
        /// </summary>
        [Test]
        public void StatIds_AreAGaplessRunFromZero()
        {
            var values = (StatId[])Enum.GetValues(typeof(StatId));

            Assert.That(values.Length, Is.EqualTo(StatIds.Count), "StatIds.Count is out of date");

            for (var i = 0; i < values.Length; i++)
            {
                Assert.That((int)values[i], Is.EqualTo(i), $"{values[i]} is not at index {i}");
            }
        }

        [Test]
        public void StatsAll_ListsEveryStatExactlyOnce()
        {
            var values = (StatId[])Enum.GetValues(typeof(StatId));

            Assert.That(StatIds.All, Is.EquivalentTo(values));
            Assert.That(StatIds.All, Is.Unique);
        }

        [Test]
        public void EveryStatHasADefinition_InItsOwnSlot()
        {
            foreach (var stat in StatIds.All)
            {
                Assert.That(StatCatalog.Default[stat].Id, Is.EqualTo(stat));
                Assert.That(StatCatalog.Default[stat].Key, Is.Not.Empty);
                Assert.That(StatCatalog.Default[stat].DisplayName, Is.Not.Empty);
            }
        }

        [Test]
        public void TheShippedCatalog_HasNothingWrongWithIt()
        {
            Assert.That(StatCatalog.Default.Validate(), Is.Empty);
        }

        [Test]
        public void Validation_CatchesADerivedStatWithNoSoftCap()
        {
            var broken = new StatCatalog(new[]
            {
                new StatDefinition(StatId.Haste, "haste", "Haste", StatKind.Asymptotic, 0f, 0f)
            });

            Assert.That(broken.Validate(), Is.Not.Empty);
        }

        [Test]
        public void Validation_CatchesABaseValueBelowItsOwnMinimum()
        {
            var broken = new StatCatalog(new[]
            {
                new StatDefinition(StatId.MaxHealth, "max_health", "Max Health", StatKind.Linear, -5f, 0f, 1f)
            });

            Assert.That(broken.Validate(), Is.Not.Empty);
        }

        [Test]
        public void TuningOverridesTheCompiledDefaults()
        {
            var config = new TuningConfig(new DictionaryTuningSource("test", new Dictionary<string, string>
            {
                { "stat.damage.base", "42" },
                { "stat.haste.soft_cap", "250" }
            }));

            var catalog = StatCatalog.FromTuning(config);

            Assert.That(catalog[StatId.Damage].BaseValue, Is.EqualTo(42f));
            Assert.That(catalog[StatId.Haste].SoftCap, Is.EqualTo(250f));
        }

        [Test]
        public void AStatMissingFromTuning_KeepsItsDefaultAndIsReported()
        {
            var config = new TuningConfig(new DictionaryTuningSource("test", new Dictionary<string, string>()));

            var catalog = StatCatalog.FromTuning(config);

            Assert.That(catalog[StatId.Damage].BaseValue, Is.EqualTo(StatCatalog.Default[StatId.Damage].BaseValue));
            Assert.That(config.MissingKeys, Is.Not.Empty);
        }

        /// <summary>
        /// Adding a stat and forgetting the config line is the one mistake this design invites: the
        /// game runs, the number comes from a compiled constant, and the balance file that is
        /// supposed to hold every number quietly does not.
        /// </summary>
        [Test]
        public void TheShippedTuningFile_DefinesEveryStat()
        {
            var directory = Path.Combine(Application.streamingAssetsPath, TuningLoader.DirectoryName);

            Assert.That(Directory.Exists(directory), $"No shipped tuning directory at {directory}");

            var config = TuningLoader.Load(new[] { directory }, out var errors);

            Assert.That(errors, Is.Empty);

            var keys = config.AllKeys;

            foreach (var definition in StatCatalog.Default.Definitions)
            {
                Assert.That(keys, Contains.Item(StatCatalog.BaseKey(definition)), definition.Key);

                if (definition.IsDerived)
                {
                    Assert.That(keys, Contains.Item(StatCatalog.SoftCapKey(definition)), definition.Key);
                }
            }
        }
    }
}
