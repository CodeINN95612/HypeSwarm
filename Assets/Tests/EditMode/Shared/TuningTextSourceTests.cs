using HypeSwarm.Shared.Tuning;
using NUnit.Framework;

namespace HypeSwarm.Shared.Tests
{
    [TestFixture]
    public sealed class TuningTextSourceTests
    {
        [Test]
        public void Parse_ReadsKeyValuePairs()
        {
            var source = TuningTextSource.Parse("test", "wave.density = 12\nwave.elites=3", out var errors);

            Assert.That(errors, Is.Empty);
            Assert.That(source.TryGetValue("wave.density", out var density), Is.True);
            Assert.That(density, Is.EqualTo("12"));
            Assert.That(source.TryGetValue("wave.elites", out var elites), Is.True);
            Assert.That(elites, Is.EqualTo("3"));
        }

        [Test]
        public void Parse_IgnoresCommentsAndBlankLines()
        {
            const string text = "# a comment\n\n   \n   # indented comment\nwave.density = 12\n";

            var source = TuningTextSource.Parse("test", text, out var errors);

            Assert.That(errors, Is.Empty);
            Assert.That(source.Count, Is.EqualTo(1));
        }

        [Test]
        public void Parse_TrimsWhitespaceAroundKeysAndValues()
        {
            var source = TuningTextSource.Parse("test", "   wave.density    =    12   ", out _);

            Assert.That(source.TryGetValue("wave.density", out var value), Is.True);
            Assert.That(value, Is.EqualTo("12"));
        }

        [Test]
        public void Parse_HandlesWindowsLineEndings()
        {
            var source = TuningTextSource.Parse("test", "wave.density = 12\r\nwave.elites = 3\r\n", out var errors);

            Assert.That(errors, Is.Empty);
            Assert.That(source.TryGetValue("wave.density", out var value), Is.True);
            Assert.That(value, Is.EqualTo("12"), "a trailing \\r must not become part of the value");
        }

        [Test]
        public void Parse_KeepsValueSeparatorsInsideTheValue()
        {
            var source = TuningTextSource.Parse("test", "a.range = 0=1", out _);

            Assert.That(source.TryGetValue("a.range", out var value), Is.True);
            Assert.That(value, Is.EqualTo("0=1"), "only the first '=' separates key from value");
        }

        [Test]
        public void Parse_ReportsLineWithNoAssignment()
        {
            TuningTextSource.Parse("core.tuning", "wave.density 12", out var errors);

            Assert.That(errors, Has.Exactly(1).Contains("core.tuning:1"));
        }

        [Test]
        public void Parse_ReportsMissingKeyAndMissingValue()
        {
            TuningTextSource.Parse("core.tuning", "= 12\nwave.density =", out var errors);

            Assert.That(errors, Has.Count.EqualTo(2));
        }

        /// <summary>
        /// A key set twice in one file is always a mistake — the second line wins and the first
        /// edit appears to have done nothing, which is a miserable thing to debug by eye.
        /// </summary>
        [Test]
        public void Parse_ReportsDuplicateKeyWithinOneFile()
        {
            var source = TuningTextSource.Parse("core.tuning", "a.b = 1\na.b = 2", out var errors);

            Assert.That(errors, Has.Exactly(1).Contains("a.b"));
            Assert.That(source.TryGetValue("a.b", out var value), Is.True);
            Assert.That(value, Is.EqualTo("2"), "the later line still wins, so the file loads");
        }

        [Test]
        public void Parse_ErrorsCarryTheSourceAndLineNumber()
        {
            TuningTextSource.Parse("10-core.tuning", "ok.key = 1\n\nbroken line", out var errors);

            Assert.That(errors, Has.Exactly(1).Contains("10-core.tuning:3"));
        }

        [Test]
        public void Parse_EmptyTextGivesAnEmptySource()
        {
            var source = TuningTextSource.Parse("test", "", out var errors);

            Assert.That(errors, Is.Empty);
            Assert.That(source.Count, Is.Zero);
        }
    }
}
