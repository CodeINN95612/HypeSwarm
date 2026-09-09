using HypeSwarm.Shared.Content;
using HypeSwarm.Shared.Net;
using HypeSwarm.Shared.Stats;
using Mirror;
using NUnit.Framework;

namespace HypeSwarm.Shared.Tests
{
    /// <summary>
    /// The hand-written wire format for a stat modifier.
    /// </summary>
    /// <remarks>
    /// A modifier that does not survive the round trip is a buff that works on the host and does
    /// nothing on the other four machines, with nothing logged anywhere — the format is worth a test
    /// precisely because getting it wrong is silent.
    ///
    /// <para>The extension methods are called directly rather than through Mirror generic
    /// <c>Write&lt;T&gt;</c>. That indirection is filled in by weaver-generated code which runs from
    /// <c>[RuntimeInitializeOnLoadMethod]</c>, and edit mode tests never enter play mode, so in here
    /// it would be null for every type in the project. What is under test is our format anyway;
    /// whether Mirror finds it is settled at compile time, by the sync list in
    /// <see cref="ChampionStats"/> refusing to weave without it.</para>
    /// </remarks>
    [TestFixture]
    public sealed class StatSerializationTests
    {
        static StatModifier RoundTrip(StatModifier modifier)
        {
            var writer = new NetworkWriter();

            writer.WriteStatModifier(modifier);

            return new NetworkReader(writer.ToArraySegment()).ReadStatModifier();
        }

        static ContentId RoundTrip(ContentId id)
        {
            var writer = new NetworkWriter();

            writer.WriteContentId(id);

            return new NetworkReader(writer.ToArraySegment()).ReadContentId();
        }

        [Test]
        public void AModifier_SurvivesTheRoundTrip()
        {
            var source = ModifierSource.Parse("augment.shield_amp", 3);
            var modifier = StatModifier.PercentMult(StatId.DamageReduction, 0.125f, source);

            Assert.That(RoundTrip(modifier), Is.EqualTo(modifier));
        }

        [Test]
        public void EveryStatAndOperation_SurvivesTheRoundTrip()
        {
            var source = ModifierSource.Parse("item.plate");

            foreach (var stat in StatIds.All)
            {
                foreach (ModifierOperation operation in System.Enum.GetValues(typeof(ModifierOperation)))
                {
                    var modifier = new StatModifier(stat, operation, -2.5f, source);

                    Assert.That(RoundTrip(modifier), Is.EqualTo(modifier), $"{stat} {operation}");
                }
            }
        }

        [Test]
        public void AContentId_SurvivesTheRoundTrip()
        {
            var id = ContentId.Parse("item.iron_ring");

            Assert.That(RoundTrip(id), Is.EqualTo(id));
        }

        /// <summary>
        /// A default id is what a modifier from an unknown source carries. It has to come back as
        /// invalid rather than as an exception that drops the connection.
        /// </summary>
        [Test]
        public void AnInvalidContentId_ComesBackInvalid()
        {
            Assert.That(RoundTrip(default(ContentId)).IsValid, Is.False);
        }
    }
}
