using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using HypeSwarm.Shared.Tuning;
using NUnit.Framework;

namespace HypeSwarm.Shared.Tests
{
    [TestFixture]
    public sealed class TuningConfigTests
    {
        static DictionaryTuningSource Source(string name, params (string Key, string Value)[] entries)
        {
            var source = new DictionaryTuningSource(name);

            foreach (var entry in entries)
            {
                source.Set(entry.Key, entry.Value);
            }

            return source;
        }

        [Test]
        public void GetFloat_ReadsAValue()
        {
            var config = new TuningConfig(Source("test", ("wave.density", "12.5")));

            Assert.That(config.GetFloat("wave.density", 0f), Is.EqualTo(12.5f));
        }

        [Test]
        public void LaterLayersWin()
        {
            var config = new TuningConfig(
                Source("shipped", ("wave.density", "12"), ("wave.elites", "3")),
                Source("override", ("wave.density", "20")));

            Assert.That(config.GetFloat("wave.density", 0f), Is.EqualTo(20f), "the override layer wins");
            Assert.That(config.GetFloat("wave.elites", 0f), Is.EqualTo(3f), "keys it does not define fall through");
        }

        [Test]
        public void TryGetRaw_NamesTheLayerItCameFrom()
        {
            var config = new TuningConfig(
                Source("shipped", ("wave.density", "12")),
                Source("override", ("wave.density", "20")));

            Assert.That(config.TryGetRaw("wave.density", out var value, out var source), Is.True);
            Assert.That(value, Is.EqualTo("20"));
            Assert.That(source, Is.EqualTo("override"), "'which file set this?' is the first question when a number looks wrong");
        }

        /// <summary>
        /// The bug this pins is quiet and locale-dependent: under a Spanish or German culture the
        /// decimal separator is ',', so a default-culture parse of "1.5" yields 15. The config file
        /// would be correct, the code would be correct, and the game would be ten times too fast on
        /// half the machines that run it.
        /// </summary>
        [Test]
        public void Parsing_IsInvariantCulture_RegardlessOfSystemLocale()
        {
            var original = Thread.CurrentThread.CurrentCulture;

            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("es-ES");

                var config = new TuningConfig(Source("test",
                    ("a.float", "1.5"),
                    ("a.curve", "0.5, 1.5")));

                Assert.That(config.GetFloat("a.float", 0f), Is.EqualTo(1.5f));
                Assert.That(config.GetFloats("a.curve", null), Is.EqualTo(new[] { 0.5f, 1.5f }));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }

        [Test]
        public void MissingKey_ReturnsFallbackAndIsRecorded()
        {
            var config = new TuningConfig(Source("test"));

            Assert.That(config.GetFloat("wave.absent", 9f), Is.EqualTo(9f));
            Assert.That(config.MissingKeys, Does.Contain("wave.absent"));
            Assert.That(config.Validate(), Is.Not.Empty, "a silent fallback is a balance bug nobody can find");
        }

        [Test]
        public void MalformedValue_ReturnsFallbackAndIsRecorded()
        {
            var config = new TuningConfig(Source("test", ("wave.density", "twelve")));

            Assert.That(config.GetFloat("wave.density", 9f), Is.EqualTo(9f));
            Assert.That(config.MalformedEntries, Has.Exactly(1).Contains("wave.density"));
        }

        [Test]
        public void MalformedValue_IsDistinguishedFromAMissingOne()
        {
            var config = new TuningConfig(Source("test", ("wave.density", "twelve")));

            config.GetFloat("wave.density", 0f);

            Assert.That(config.MissingKeys, Is.Empty, "the key exists; it is the value that is wrong");
        }

        [Test]
        public void GetInt_ReadsWholeNumbersAndRejectsFractions()
        {
            var config = new TuningConfig(Source("test", ("a.int", "7"), ("a.fraction", "7.5")));

            Assert.That(config.GetInt("a.int", 0), Is.EqualTo(7));
            Assert.That(config.GetInt("a.fraction", -1), Is.EqualTo(-1));
            Assert.That(config.MalformedEntries, Has.Exactly(1).Contains("a.fraction"));
        }

        [TestCase("true", true)]
        [TestCase("TRUE", true)]
        [TestCase("1", true)]
        [TestCase("false", false)]
        [TestCase("False", false)]
        [TestCase("0", false)]
        public void GetBool_AcceptsTheObviousSpellings(string raw, bool expected)
        {
            var config = new TuningConfig(Source("test", ("a.flag", raw)));

            Assert.That(config.GetBool("a.flag", !expected), Is.EqualTo(expected));
        }

        [Test]
        public void GetBool_RejectsAnythingElse()
        {
            var config = new TuningConfig(Source("test", ("a.flag", "yes")));

            Assert.That(config.GetBool("a.flag", false), Is.False);
            Assert.That(config.MalformedEntries, Has.Exactly(1).Contains("a.flag"));
        }

        [Test]
        public void GetFloats_ReadsACommaSeparatedCurve()
        {
            var config = new TuningConfig(Source("test", ("siege.health", "4000, 6000,9000")));

            Assert.That(config.GetFloats("siege.health", null), Is.EqualTo(new[] { 4000f, 6000f, 9000f }));
        }

        /// <summary>
        /// A curve that silently drops its broken entry is worse than one that refuses: the game
        /// runs, the numbers are subtly wrong, and nothing points at the config file.
        /// </summary>
        [Test]
        public void GetFloats_RejectsTheWholeListWhenOneEntryIsMalformed()
        {
            var fallback = new[] { 1f };
            var config = new TuningConfig(Source("test", ("siege.health", "4000, oops, 9000")));

            Assert.That(config.GetFloats("siege.health", fallback), Is.SameAs(fallback));
            Assert.That(config.MalformedEntries, Has.Exactly(1).Contains("siege.health"));
        }

        [Test]
        public void AllKeys_UnionsEveryLayer()
        {
            var config = new TuningConfig(
                Source("shipped", ("a.one", "1"), ("a.two", "2")),
                Source("override", ("a.two", "22"), ("a.three", "3")));

            Assert.That(config.AllKeys, Is.EquivalentTo(new[] { "a.one", "a.two", "a.three" }));
        }

        [Test]
        public void Validate_IsEmptyForACleanRead()
        {
            var config = new TuningConfig(Source("test", ("a.float", "1.5")));

            config.GetFloat("a.float", 0f);

            Assert.That(config.Validate(), Is.Empty);
        }

        [Test]
        public void EmptyConfig_FallsBackForEverything()
        {
            var config = new TuningConfig(new List<ITuningSource>());

            Assert.That(config.GetFloat("anything", 3f), Is.EqualTo(3f));
            Assert.That(config.GetInt("anything", 3), Is.EqualTo(3));
            Assert.That(config.GetBool("anything", true), Is.True);
        }
    }
}
