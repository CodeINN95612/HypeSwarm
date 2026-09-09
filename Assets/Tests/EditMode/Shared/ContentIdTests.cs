using System;
using HypeSwarm.Shared.Content;
using NUnit.Framework;

namespace HypeSwarm.Shared.Tests
{
    [TestFixture]
    public sealed class ContentIdTests
    {
        [TestCase("augment.shield_amp")]
        [TestCase("item.weapon.bouncy_ball")]
        [TestCase("champion.a1")]
        [TestCase("rune.gold_per_kill")]
        public void Parse_AcceptsWellFormedIds(string raw)
        {
            var id = ContentId.Parse(raw);

            Assert.That(id.IsValid, Is.True);
            Assert.That(id.Value, Is.EqualTo(raw));
        }

        [TestCase(null, TestName = "null")]
        [TestCase("", TestName = "empty")]
        [TestCase("augment", TestName = "no category segment")]
        [TestCase("Augment.Shield", TestName = "uppercase")]
        [TestCase("augment.shield amp", TestName = "space")]
        [TestCase("augment.shield-amp", TestName = "hyphen")]
        [TestCase("augment.escudo_ñ", TestName = "non-ascii")]
        [TestCase(".shield_amp", TestName = "leading separator")]
        [TestCase("augment.", TestName = "trailing separator")]
        [TestCase("augment..shield", TestName = "empty middle segment")]
        public void TryParse_RejectsMalformedIds(string raw)
        {
            Assert.That(ContentId.TryParse(raw, out _, out var error), Is.False);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void TryParse_RejectsIdsOverMaxLength()
        {
            var tooLong = "augment." + new string('a', ContentId.MaxLength);

            Assert.That(ContentId.TryParse(tooLong, out _), Is.False);
        }

        [Test]
        public void Parse_ErrorMessageNamesTheOffendingCharacter()
        {
            ContentId.TryParse("augment.shield-amp", out _, out var error);

            Assert.That(error, Does.Contain("-"), "The author needs to be told which character is wrong.");
        }

        [Test]
        public void Parse_ThrowsOnMalformedId()
        {
            Assert.Throws<ArgumentException>(() => ContentId.Parse("NotValid"));
        }

        [Test]
        public void Category_IsTheLeadingSegment()
        {
            Assert.That(ContentId.Parse("item.weapon.bouncy_ball").Category, Is.EqualTo("item"));
        }

        [Test]
        public void Default_IsInvalidAndDoesNotThrow()
        {
            var id = default(ContentId);

            Assert.That(id.IsValid, Is.False);
            Assert.That(id.Value, Is.Empty);
            Assert.That(id.Category, Is.Empty);
            Assert.That(id.ToString(), Is.Empty);
        }

        [Test]
        public void Equality_IsByValue()
        {
            var a = ContentId.Parse("augment.shield_amp");
            var b = ContentId.Parse("augment.shield_amp");
            var c = ContentId.Parse("augment.shield_cap");

            Assert.That(a, Is.EqualTo(b));
            Assert.That(a == b, Is.True);
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
            Assert.That(a != c, Is.True);
        }

        /// <summary>
        /// Network indices are assigned from sorted id order, so this comparison decides the wire
        /// protocol. A culture-sensitive comparison would give two machines in different locales
        /// different index tables for identical content — and the symptom would be a client
        /// applying the wrong augment, which reads as a gameplay bug rather than a sorting one.
        /// </summary>
        [Test]
        public void CompareTo_IsOrdinal()
        {
            var ids = new[]
            {
                "augment.a_b",
                "augment.ab",
                "augment.a0",
                "augment.a_0",
                "item.a",
                "item._a"
            };

            for (var i = 0; i < ids.Length; i++)
            {
                for (var j = 0; j < ids.Length; j++)
                {
                    var expected = Math.Sign(string.CompareOrdinal(ids[i], ids[j]));
                    var actual = Math.Sign(ContentId.Parse(ids[i]).CompareTo(ContentId.Parse(ids[j])));

                    Assert.That(actual, Is.EqualTo(expected), $"'{ids[i]}' vs '{ids[j]}'");
                }
            }
        }
    }
}
