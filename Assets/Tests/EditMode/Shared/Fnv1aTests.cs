using HypeSwarm.Shared.Hashing;
using NUnit.Framework;

namespace HypeSwarm.Shared.Tests
{
    [TestFixture]
    public sealed class Fnv1aTests
    {
        /// <summary>
        /// Published FNV-1a 32-bit vectors. These matter more than they look: without them the
        /// implementation could be any self-consistent hash, and "self-consistent" is exactly what
        /// a hash used across machines and builds must not merely be. If someone later "optimises"
        /// this into a different mixing function, saves and manifests silently stop matching older
        /// builds — these vectors are what catches that.
        /// </summary>
        [TestCase("", 0x811c9dc5u)]
        [TestCase("a", 0xe40c292cu)]
        [TestCase("b", 0xe70c2de5u)]
        [TestCase("foobar", 0xbf9cf968u)]
        public void Hash_MatchesPublishedVectors(string text, uint expected)
        {
            Assert.That(Fnv1a.Hash(text), Is.EqualTo(expected));
        }

        [Test]
        public void Hash_IsSeedable_SoASequenceHashesAsOneStream()
        {
            var streamed = Fnv1a.Hash("bar", Fnv1a.Hash("foo"));

            Assert.That(streamed, Is.EqualTo(Fnv1a.Hash("foobar")));
        }

        [Test]
        public void Hash_OfEmptyStringIsTheSeed()
        {
            Assert.That(Fnv1a.Hash("", 12345u), Is.EqualTo(12345u));
            Assert.That(Fnv1a.Hash((string)null, 12345u), Is.EqualTo(12345u));
        }

        [Test]
        public void Hash_DiffersForDifferentInput()
        {
            Assert.That(Fnv1a.Hash("augment.shield_amp"), Is.Not.EqualTo(Fnv1a.Hash("augment.shield_cap")));
        }
    }
}
