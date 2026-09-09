using HypeSwarm.Shared.Hashing;

namespace HypeSwarm.Shared.Combat
{
    /// <summary>
    /// The random numbers combat rolls, as a seeded stream rather than a global one.
    /// </summary>
    /// <remarks>
    /// Not <c>UnityEngine.Random</c>, which is one shared mutable stream: a dodge roll drawn from it
    /// depends on how many particles spawned that frame, which makes a combat bug impossible to
    /// reproduce and a test impossible to write. An explicit stream per entity means the same seed
    /// and the same sequence of hits produce the same outcome, on the host and in a replay.
    ///
    /// <para>Xorshift32. Small, fast, no allocation, and good enough for whether a hit was dodged —
    /// this is not a place that needs a cryptographic generator or the state of a Mersenne twister
    /// on every one of two thousand enemies.</para>
    ///
    /// <para>Rolls happen on the host only (§10). Damage is host-authoritative, so no client ever
    /// needs to reproduce this stream, and it does not have to be part of the shared spawn seed.</para>
    /// </remarks>
    public sealed class DamageRandom
    {
        /// <summary>Any non-zero constant. Xorshift is stuck forever if its state reaches zero.</summary>
        const uint FallbackSeed = 0x9E3779B9u;

        uint state;

        public DamageRandom(uint seed)
        {
            state = seed == 0u ? FallbackSeed : seed;
        }

        /// <summary>The next raw value. Never zero.</summary>
        public uint NextUInt()
        {
            var next = state;

            next ^= next << 13;
            next ^= next >> 17;
            next ^= next << 5;

            state = next;

            return next;
        }

        /// <summary>The next roll, in <c>[0, 1)</c>. Compare against a derived chance directly.</summary>
        public float NextFloat()
        {
            // Twenty-four bits, which is every value a float can represent in this range without
            // rounding two of them onto the same number.
            return (NextUInt() >> 8) * (1f / 16777216f);
        }

        /// <summary>
        /// A stream seed for one entity. Hashed rather than used directly so that two entities
        /// spawned back to back do not start one step apart and roll near-identical sequences.
        /// </summary>
        public static uint SeedFrom(uint netId, uint runSeed = Fnv1a.OffsetBasis)
        {
            unchecked
            {
                var mixed = runSeed;

                for (var shift = 0; shift < 32; shift += 8)
                {
                    mixed = (mixed ^ ((netId >> shift) & 0xFFu)) * Fnv1a.Prime;
                }

                return mixed;
            }
        }
    }
}
