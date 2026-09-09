using System;
using System.Collections.Generic;
using UnityEngine;

namespace HypeSwarm.Shared.Content
{
    /// <summary>
    /// The authored list of content assets that goes into a session's <see cref="ContentRegistry"/>.
    /// </summary>
    /// <remarks>
    /// One explicit list rather than a scan of the project, because the registry defines the
    /// network index table: an asset appearing or disappearing from a folder must not silently
    /// renumber content for one peer. Adding content is a deliberate edit to this asset.
    /// </remarks>
    [CreateAssetMenu(menuName = "Hype Swarm/Content Library", fileName = "ContentLibrary")]
    public sealed class ContentLibrary : ScriptableObject
    {
        [SerializeField]
        [Tooltip("Every piece of content in the build. Order is irrelevant — network indices come " +
                 "from sorted ids, not from this list.")]
        List<ContentDefinition> definitions = new List<ContentDefinition>();

        public IReadOnlyList<ContentDefinition> Definitions => definitions;

        /// <summary>
        /// Builds a registry from this library, sealed and ready for a session by default.
        /// </summary>
        public ContentRegistry BuildRegistry(bool seal = true)
        {
            var registry = new ContentRegistry();

            for (var i = 0; i < definitions.Count; i++)
            {
                var definition = definitions[i];

                if (definition == null)
                {
                    throw new InvalidOperationException(
                        $"Content library '{name}' has an empty slot at index {i}. " +
                        "An empty slot usually means a deleted asset — remove the slot or restore the asset.");
                }

                if (!definition.TryValidateId(out var error))
                {
                    throw new InvalidOperationException($"Content library '{name}', '{definition.name}': {error}");
                }

                registry.Register(definition);
            }

            if (seal)
            {
                registry.Seal();
            }

            return registry;
        }
    }
}
