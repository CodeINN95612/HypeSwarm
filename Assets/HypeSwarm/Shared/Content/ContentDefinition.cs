using System;
using UnityEngine;

namespace HypeSwarm.Shared.Content
{
    /// <summary>
    /// Base class for authored content assets. Carries the stable string id (spec §13.2) and
    /// nothing else — subclasses add the data their content type needs.
    /// </summary>
    /// <remarks>
    /// <b>Never put runtime state on a subclass of this.</b> A ScriptableObject asset is one shared
    /// instance: two players on the same champion would share the field. Cooldowns, charges and
    /// stacks belong on the per-owner runtime object, not here (spec §8.1).
    /// </remarks>
    public abstract class ContentDefinition : ScriptableObject, IContentDefinition
    {
        [SerializeField]
        [Tooltip("Stable id, 'category.name' — e.g. augment.shield_amp.\n\n" +
                 "Frozen once this content ships: save data and the network protocol both address " +
                 "content by this string, so renaming it invalidates existing saves.")]
        string id;

        // Memoised parse of the serialised field above. This is not the runtime state the class
        // docs forbid — it is a pure function of authored data, identical for every reader, and
        // discarded on domain reload.
        [NonSerialized] ContentId parsedId;
        [NonSerialized] bool parsedIdValid;

        /// <summary>The raw serialised text, valid or not. For error messages and editor tooling.</summary>
        public string RawId => id;

        /// <summary>
        /// The parsed id. Returns an invalid <see cref="ContentId"/> when the serialised text is
        /// malformed rather than throwing — <see cref="ContentRegistry.Register"/> is the choke
        /// point that reports it, so one bad asset names itself instead of breaking every load.
        /// </summary>
        public ContentId Id
        {
            get
            {
                if (!parsedIdValid)
                {
                    ContentId.TryParse(id, out parsedId);
                    parsedIdValid = true;
                }

                return parsedId;
            }
        }

        /// <summary>Explains what is wrong with the serialised id, for validation tooling.</summary>
        public bool TryValidateId(out string error)
        {
            return ContentId.TryParse(id, out _, out error);
        }

        protected virtual void OnValidate()
        {
            parsedIdValid = false;
        }
    }
}
