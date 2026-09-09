using System;
using System.Collections.Generic;
using HypeSwarm.Shared.Hashing;

namespace HypeSwarm.Shared.Content
{
    /// <summary>
    /// The single lookup from a stable string id to the content it names, plus the id-to-index
    /// table the network protocol uses (spec §13.2).
    /// </summary>
    /// <remarks>
    /// <b>Lifecycle: fill, then seal, then read.</b> Content is registered once at session start;
    /// <see cref="Seal"/> then freezes the set and assigns each id a <see cref="ushort"/> network
    /// index from ordinal-sorted order. After that the host sends indices rather than strings, and
    /// every peer resolves them through an identical table.
    ///
    /// <para>Both halves of that lifecycle are enforced rather than documented: registering after
    /// sealing throws, and asking for an index before sealing throws. Silently allowing either is
    /// how peers end up with tables that disagree by one entry.</para>
    ///
    /// <para><see cref="ManifestHash"/> is what makes the disagreement detectable — peers compare
    /// it during the join handshake and refuse a mismatch, instead of discovering the problem later
    /// as one client applying the wrong augment.</para>
    /// </remarks>
    public sealed class ContentRegistry
    {
        readonly Dictionary<ContentId, IContentDefinition> byId = new Dictionary<ContentId, IContentDefinition>();

        ContentId[] orderedIds;
        Dictionary<ContentId, ushort> indexById;
        uint manifestHash;

        /// <summary>How many definitions are registered.</summary>
        public int Count => byId.Count;

        /// <summary>True once <see cref="Seal"/> has run and network indices exist.</summary>
        public bool IsSealed => orderedIds != null;

        /// <summary>
        /// Every id in network-index order — <c>OrderedIds[i]</c> is the id whose index is
        /// <c>i</c>. Sealed registries only.
        /// </summary>
        public IReadOnlyList<ContentId> OrderedIds
        {
            get
            {
                RequireSealed(nameof(OrderedIds));
                return orderedIds;
            }
        }

        /// <summary>
        /// A deterministic hash over the sealed id set. Identical content gives an identical hash
        /// on every machine, so peers can compare one number during the join handshake instead of
        /// exchanging the whole manifest. Sealed registries only.
        /// </summary>
        public uint ManifestHash
        {
            get
            {
                RequireSealed(nameof(ManifestHash));
                return manifestHash;
            }
        }

        /// <summary>
        /// Adds a definition. Throws on a duplicate or malformed id — both are authoring mistakes
        /// that must surface at load, not at the moment the content is first used.
        /// </summary>
        public void Register(IContentDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (IsSealed)
            {
                throw new InvalidOperationException(
                    $"Cannot register '{definition.Id}': the registry is sealed. All content must be " +
                    "registered before Seal(), or peers will disagree about network indices.");
            }

            var id = definition.Id;

            if (!id.IsValid)
            {
                throw new ArgumentException(
                    $"Cannot register a {definition.GetType().Name} with a malformed content id. " +
                    "Expected 'category.name' in lowercase.",
                    nameof(definition));
            }

            if (byId.TryGetValue(id, out var existing))
            {
                throw new ArgumentException(
                    $"Duplicate content id '{id}': already registered by a {existing.GetType().Name}. " +
                    "Ids are the addressing scheme for saves and the network protocol, so they must be unique.",
                    nameof(definition));
            }

            byId.Add(id, definition);
        }

        /// <summary>Registers every definition in <paramref name="definitions"/>, in order.</summary>
        public void RegisterAll(IEnumerable<IContentDefinition> definitions)
        {
            if (definitions == null)
            {
                throw new ArgumentNullException(nameof(definitions));
            }

            foreach (var definition in definitions)
            {
                Register(definition);
            }
        }

        /// <summary>
        /// Freezes the content set and assigns network indices from ordinal-sorted id order.
        /// Registration order therefore cannot affect the resulting table.
        /// </summary>
        public void Seal()
        {
            if (IsSealed)
            {
                throw new InvalidOperationException("The content registry is already sealed.");
            }

            if (byId.Count > ushort.MaxValue)
            {
                throw new InvalidOperationException(
                    $"{byId.Count} pieces of content exceed the {ushort.MaxValue} addressable by a ushort " +
                    "network index. Widen the index type before adding more.");
            }

            var ids = new ContentId[byId.Count];
            byId.Keys.CopyTo(ids, 0);

            // Ordinal sort (ContentId.CompareTo). Dictionary enumeration order is not a contract,
            // so this is what makes the index table reproducible.
            Array.Sort(ids);

            indexById = new Dictionary<ContentId, ushort>(ids.Length);
            var hash = Fnv1a.OffsetBasis;

            for (var i = 0; i < ids.Length; i++)
            {
                indexById[ids[i]] = (ushort)i;

                // The separator keeps the hash unambiguous: "a.bc" + "d" and "a.b" + "cd" would
                // otherwise hash identically.
                hash = Fnv1a.Hash(ids[i].Value, hash);
                hash = Fnv1a.Hash("\n", hash);
            }

            manifestHash = hash;
            orderedIds = ids;
        }

        public bool Contains(ContentId id)
        {
            return byId.ContainsKey(id);
        }

        public bool TryGet(ContentId id, out IContentDefinition definition)
        {
            return byId.TryGetValue(id, out definition);
        }

        public bool TryGet<T>(ContentId id, out T definition) where T : class, IContentDefinition
        {
            if (byId.TryGetValue(id, out var found) && found is T typed)
            {
                definition = typed;
                return true;
            }

            definition = null;
            return false;
        }

        /// <summary>
        /// Resolves an id, throwing if it is missing or the wrong type. Use where a miss is a bug;
        /// use <see cref="TryGet{T}"/> where it is a possibility.
        /// </summary>
        public T Get<T>(ContentId id) where T : class, IContentDefinition
        {
            if (!byId.TryGetValue(id, out var found))
            {
                throw new KeyNotFoundException($"No content registered for id '{id}'.");
            }

            if (found is T typed)
            {
                return typed;
            }

            throw new InvalidCastException(
                $"Content '{id}' is a {found.GetType().Name}, not a {typeof(T).Name}.");
        }

        /// <summary>The network index for an id. Sealed registries only.</summary>
        public ushort IndexOf(ContentId id)
        {
            RequireSealed(nameof(IndexOf));

            if (!indexById.TryGetValue(id, out var index))
            {
                throw new KeyNotFoundException($"No content registered for id '{id}'.");
            }

            return index;
        }

        public bool TryGetIndex(ContentId id, out ushort index)
        {
            RequireSealed(nameof(TryGetIndex));
            return indexById.TryGetValue(id, out index);
        }

        /// <summary>The id for a network index. Sealed registries only.</summary>
        public ContentId FromIndex(ushort index)
        {
            RequireSealed(nameof(FromIndex));

            if (index >= orderedIds.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(index),
                    index,
                    $"Network index {index} is outside the {orderedIds.Length} registered entries. " +
                    "This usually means the peers' content manifests disagree — compare ManifestHash on join.");
            }

            return orderedIds[index];
        }

        void RequireSealed(string member)
        {
            if (!IsSealed)
            {
                throw new InvalidOperationException(
                    $"{member} is only available on a sealed registry. Call Seal() once all content is registered.");
            }
        }
    }
}
