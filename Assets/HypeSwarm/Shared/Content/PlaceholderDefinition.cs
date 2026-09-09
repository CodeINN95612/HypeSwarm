using UnityEngine;

namespace HypeSwarm.Shared.Content
{
    /// <summary>
    /// Scaffolding: the simplest possible <see cref="ContentDefinition"/>, so the Phase 1 exit
    /// criterion — a placeholder content id resolving through the registry — can be demonstrated
    /// with a real authored asset before any real content type exists.
    /// </summary>
    /// <remarks>
    /// Delete this once augments, items, and abilities have their own definition types. It exists
    /// to prove the pipeline, not to be built on.
    /// </remarks>
    [CreateAssetMenu(menuName = "Hype Swarm/Placeholder Content", fileName = "PlaceholderContent")]
    public sealed class PlaceholderDefinition : ContentDefinition
    {
        [SerializeField]
        [TextArea]
        [Tooltip("What this placeholder stands in for.")]
        string notes;

        public string Notes => notes;
    }
}
