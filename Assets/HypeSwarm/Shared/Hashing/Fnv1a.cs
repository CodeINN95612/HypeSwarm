using System.Text;

namespace HypeSwarm.Shared.Hashing
{
    /// <summary>
    /// FNV-1a, 32-bit. The project's hash for anything that must agree across machines, processes,
    /// or runs — content manifests, save checksums, deterministic seeds.
    /// </summary>
    /// <remarks>
    /// <c>string.GetHashCode</c> cannot be used for any of those. It is randomised per process on
    /// modern .NET, so two clients hashing identical content get different answers and the
    /// mismatch looks like a content-version bug rather than a hashing one.
    ///
    /// FNV-1a is not cryptographic and is not meant to be. It is used where the requirement is
    /// "identical inputs give identical outputs everywhere, cheaply" — see
    /// <see cref="Content.ContentRegistry.ManifestHash"/>.
    /// </remarks>
    public static class Fnv1a
    {
        public const uint OffsetBasis = 2166136261u;
        public const uint Prime = 16777619u;

        /// <summary>
        /// Hashes the UTF-8 bytes of <paramref name="text"/>. Pass a previous result as
        /// <paramref name="seed"/> to hash a sequence of strings as one stream.
        /// </summary>
        public static uint Hash(string text, uint seed = OffsetBasis)
        {
            if (string.IsNullOrEmpty(text))
            {
                return seed;
            }

            return Hash(Encoding.UTF8.GetBytes(text), seed);
        }

        public static uint Hash(byte[] bytes, uint seed = OffsetBasis)
        {
            var hash = seed;

            unchecked
            {
                for (var i = 0; i < bytes.Length; i++)
                {
                    hash ^= bytes[i];
                    hash *= Prime;
                }
            }

            return hash;
        }
    }
}
