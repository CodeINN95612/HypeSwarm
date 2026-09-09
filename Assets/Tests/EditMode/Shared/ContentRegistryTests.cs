using System;
using System.Collections.Generic;
using System.Linq;
using HypeSwarm.Shared.Content;
using NUnit.Framework;

namespace HypeSwarm.Shared.Tests
{
    [TestFixture]
    public sealed class ContentRegistryTests
    {
        sealed class FakeDefinition : IContentDefinition
        {
            public FakeDefinition(string id)
            {
                ContentId.TryParse(id, out var parsed);
                Id = parsed;
            }

            public ContentId Id { get; }
        }

        sealed class OtherDefinition : IContentDefinition
        {
            public OtherDefinition(string id)
            {
                Id = ContentId.Parse(id);
            }

            public ContentId Id { get; }
        }

        static ContentRegistry Filled(params string[] ids)
        {
            var registry = new ContentRegistry();

            foreach (var id in ids)
            {
                registry.Register(new FakeDefinition(id));
            }

            return registry;
        }

        [Test]
        public void Register_ThenLookupByIdReturnsTheDefinition()
        {
            var registry = new ContentRegistry();
            var definition = new FakeDefinition("augment.shield_amp");

            registry.Register(definition);

            Assert.That(registry.Contains(definition.Id), Is.True);
            Assert.That(registry.TryGet(definition.Id, out var found), Is.True);
            Assert.That(found, Is.SameAs(definition));
            Assert.That(registry.Count, Is.EqualTo(1));
        }

        [Test]
        public void Register_RejectsDuplicateId()
        {
            var registry = Filled("augment.shield_amp");

            var exception = Assert.Throws<ArgumentException>(
                () => registry.Register(new FakeDefinition("augment.shield_amp")));

            Assert.That(exception.Message, Does.Contain("augment.shield_amp"));
        }

        [Test]
        public void Register_RejectsMalformedId()
        {
            var registry = new ContentRegistry();

            Assert.Throws<ArgumentException>(() => registry.Register(new FakeDefinition("NotAnId")));
        }

        [Test]
        public void Register_RejectsNull()
        {
            Assert.Throws<ArgumentNullException>(() => new ContentRegistry().Register(null));
        }

        [Test]
        public void Get_TypedReturnsTheDefinition()
        {
            var registry = new ContentRegistry();
            var definition = new OtherDefinition("item.bouncy_ball");
            registry.Register(definition);

            Assert.That(registry.Get<OtherDefinition>(definition.Id), Is.SameAs(definition));
        }

        [Test]
        public void Get_ThrowsWithTheIdInTheMessageWhenMissing()
        {
            var registry = Filled("augment.shield_amp");

            var exception = Assert.Throws<KeyNotFoundException>(
                () => registry.Get<FakeDefinition>(ContentId.Parse("augment.absent")));

            Assert.That(exception.Message, Does.Contain("augment.absent"));
        }

        [Test]
        public void Get_ThrowsWhenTheContentIsTheWrongType()
        {
            var registry = new ContentRegistry();
            registry.Register(new FakeDefinition("item.bouncy_ball"));

            Assert.Throws<InvalidCastException>(
                () => registry.Get<OtherDefinition>(ContentId.Parse("item.bouncy_ball")));
        }

        [Test]
        public void TryGet_TypedFailsForTheWrongTypeRatherThanThrowing()
        {
            var registry = new ContentRegistry();
            registry.Register(new FakeDefinition("item.bouncy_ball"));

            Assert.That(registry.TryGet<OtherDefinition>(ContentId.Parse("item.bouncy_ball"), out var found), Is.False);
            Assert.That(found, Is.Null);
        }

        // --- Sealing -------------------------------------------------------------------

        [Test]
        public void Register_AfterSealThrows()
        {
            var registry = Filled("augment.a");
            registry.Seal();

            Assert.Throws<InvalidOperationException>(() => registry.Register(new FakeDefinition("augment.b")));
        }

        [Test]
        public void Seal_Twice_Throws()
        {
            var registry = Filled("augment.a");
            registry.Seal();

            Assert.Throws<InvalidOperationException>(() => registry.Seal());
        }

        [Test]
        public void IndexOf_BeforeSealThrows()
        {
            var registry = Filled("augment.a");

            Assert.Throws<InvalidOperationException>(() => registry.IndexOf(ContentId.Parse("augment.a")));
        }

        [Test]
        public void ManifestHash_BeforeSealThrows()
        {
            var registry = Filled("augment.a");

            Assert.Throws<InvalidOperationException>(() => _ = registry.ManifestHash);
        }

        // --- Network indices -----------------------------------------------------------

        [Test]
        public void Seal_AssignsContiguousIndicesFromZero()
        {
            var registry = Filled("augment.c", "augment.a", "augment.b");
            registry.Seal();

            var indices = registry.OrderedIds.Select(registry.IndexOf).ToArray();

            Assert.That(indices, Is.EqualTo(new ushort[] { 0, 1, 2 }));
        }

        [Test]
        public void FromIndex_RoundTripsWithIndexOf()
        {
            var registry = Filled("item.b", "augment.a", "champion.c");
            registry.Seal();

            foreach (var id in registry.OrderedIds)
            {
                Assert.That(registry.FromIndex(registry.IndexOf(id)), Is.EqualTo(id));
            }
        }

        [Test]
        public void FromIndex_OutOfRangeThrows()
        {
            var registry = Filled("augment.a");
            registry.Seal();

            Assert.Throws<ArgumentOutOfRangeException>(() => registry.FromIndex(1));
        }

        /// <summary>
        /// The host and each client build their registry independently, and nothing guarantees they
        /// enumerate content in the same order. If registration order leaked into the index table,
        /// two peers would disagree about what index 7 means — and every content reference on the
        /// wire would resolve to the wrong thing.
        /// </summary>
        [Test]
        public void Seal_IndicesAreIndependentOfRegistrationOrder()
        {
            var ids = new[] { "augment.a", "augment.b", "item.a", "item.b", "champion.a" };

            var forwards = Filled(ids);
            var backwards = Filled(ids.Reverse().ToArray());

            forwards.Seal();
            backwards.Seal();

            Assert.That(backwards.OrderedIds, Is.EqualTo(forwards.OrderedIds));

            foreach (var id in ids.Select(ContentId.Parse))
            {
                Assert.That(backwards.IndexOf(id), Is.EqualTo(forwards.IndexOf(id)), $"index of '{id}'");
            }
        }

        [Test]
        public void ManifestHash_MatchesForTheSameContentInAnyOrder()
        {
            var ids = new[] { "augment.a", "augment.b", "item.a" };

            var forwards = Filled(ids);
            var backwards = Filled(ids.Reverse().ToArray());

            forwards.Seal();
            backwards.Seal();

            Assert.That(backwards.ManifestHash, Is.EqualTo(forwards.ManifestHash));
        }

        [Test]
        public void ManifestHash_DiffersWhenContentDiffers()
        {
            var baseline = Filled("augment.a", "augment.b");
            var extra = Filled("augment.a", "augment.b", "augment.c");
            var renamed = Filled("augment.a", "augment.z");

            baseline.Seal();
            extra.Seal();
            renamed.Seal();

            Assert.That(extra.ManifestHash, Is.Not.EqualTo(baseline.ManifestHash), "an added entry must change the hash");
            Assert.That(renamed.ManifestHash, Is.Not.EqualTo(baseline.ManifestHash), "a renamed entry must change the hash");
        }

        /// <summary>
        /// Without a separator in the hashed stream, "a.bc" + "d.e" and "a.b" + "cd.e" would hash
        /// identically and two genuinely different content sets would pass the join handshake.
        /// </summary>
        [Test]
        public void ManifestHash_IsNotFooledByIdBoundaries()
        {
            var first = Filled("a.bc", "d.e");
            var second = Filled("a.b", "cd.e");

            first.Seal();
            second.Seal();

            Assert.That(second.ManifestHash, Is.Not.EqualTo(first.ManifestHash));
        }
    }
}
