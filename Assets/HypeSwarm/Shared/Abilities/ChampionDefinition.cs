using System;
using System.Collections.Generic;
using HypeSwarm.Shared.Content;
using UnityEngine;

namespace HypeSwarm.Shared.Abilities
{
    /// <summary>
    /// One authored champion: a name and five abilities.
    /// </summary>
    /// <remarks>
    /// Deliberately thin. Base stats are not here — every machine derives them from the tuned catalog
    /// (§9), and champion-authored growth arrives with the XP loop at Phase 2 step 8 (§5.6.3). What a
    /// champion <i>is</i>, mechanically, is its five abilities; anything else on this asset would be
    /// something the same champion could not be without.
    ///
    /// <para>The abilities are a list, not five named fields, because slots are indexed (§8.3). The
    /// guarantee that there is one of each role is <see cref="AbilityValidation"/>'s job, which is the
    /// right place for it: a champion mid-authoring is allowed to be incomplete, and a champion that
    /// ships is not.</para>
    /// </remarks>
    [CreateAssetMenu(menuName = "Hype Swarm/Champion", fileName = "Champion")]
    public sealed class ChampionDefinition : ContentDefinition, IChampionDefinition
    {
        [SerializeField]
        [Tooltip("What the player is shown.")]
        string displayName;

        [SerializeField]
        [TextArea]
        [Tooltip("What this champion is for, in a sentence. Authoring notes, not player-facing text.")]
        string notes;

        [SerializeField]
        [Tooltip("The five abilities, in slot order: passive, primary, secondary, mobility, ultimate.")]
        AbilityDefinition[] abilities = Array.Empty<AbilityDefinition>();

        [NonSerialized] IAbilityDefinition[] widened;

        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;

        public string Notes => notes;

        public IReadOnlyList<IAbilityDefinition> Abilities => widened ??= Widen();

        protected override void OnValidate()
        {
            base.OnValidate();

            widened = null;
        }

        IAbilityDefinition[] Widen()
        {
            if (abilities == null)
            {
                return Array.Empty<IAbilityDefinition>();
            }

            var result = new IAbilityDefinition[abilities.Length];

            for (var i = 0; i < abilities.Length; i++)
            {
                result[i] = abilities[i];
            }

            return result;
        }
    }
}
