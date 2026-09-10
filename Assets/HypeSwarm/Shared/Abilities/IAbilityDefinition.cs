using System.Collections.Generic;
using HypeSwarm.Shared.Content;

namespace HypeSwarm.Shared.Abilities
{
    /// <summary>
    /// Everything the runtime reads off an authored ability. Immutable data and nothing else (§8.1).
    /// </summary>
    /// <remarks>
    /// <b>Never put runtime state behind this interface.</b> The implementation that ships is a
    /// ScriptableObject, which is one shared instance for every champion that has the ability — two
    /// players on the same champion would share a cooldown timer. State lives on
    /// <see cref="AbilityInstance"/>, which is per owner.
    ///
    /// <para>An interface rather than the asset class directly, for the same reason
    /// <see cref="IContentDefinition"/> is one: the book, the instances, the cast state machine and
    /// validation are then all exercised in EditMode against plain C# objects, with no assets to
    /// author, no import, and no scene.</para>
    /// </remarks>
    public interface IAbilityDefinition : IContentDefinition
    {
        /// <summary>What the player is shown. Not the id, which they never see.</summary>
        string DisplayName { get; }

        /// <summary>Which of the five slots this belongs in (§5.5.3).</summary>
        AbilityRole Role { get; }

        /// <summary>What casting it costs in movement (§5.5.4).</summary>
        CastCost CastCost { get; }

        /// <summary>
        /// Seconds between the input and the effect. Zero resolves on the same frame, which is what
        /// most free-cast abilities want.
        /// </summary>
        float CastTime { get; }

        /// <summary>
        /// Longest a channel may be held, for <see cref="CastCost.Channelled"/>. The effect resolves
        /// when the player lets go or when this runs out, whichever comes first.
        /// </summary>
        float MaxChannelDuration { get; }

        /// <summary>Base cooldown in seconds, before haste.</summary>
        float Cooldown { get; }

        /// <summary>
        /// How many uses are banked. One for an ordinary ability; more is the mobility-as-a-resource
        /// model from §5.5.3.
        /// </summary>
        int Charges { get; }

        /// <summary>
        /// Seconds that must pass between two uses, independent of charges. Exists so one frame of
        /// input cannot spend two charges, which players read as the game eating an input.
        /// </summary>
        float ChargeLockout { get; }

        /// <summary>How the cast is aimed (§5.5.1).</summary>
        TargetingMode Targeting { get; }

        /// <summary>
        /// How far the aim point may be from the caster, in metres. Authored, never scaled by a stat
        /// (§5.5.5), and clamped by the host as well as the client.
        /// </summary>
        float Range { get; }

        /// <summary>
        /// What happens the moment the cast begins, before the cast time or the channel (§8.4, PreCast).
        /// </summary>
        /// <remarks>
        /// Almost always empty. It exists for the two things that have to happen <i>during</i> a
        /// commitment rather than after it: the damage reduction a channel grants while it is held, and
        /// the telegraph a wind-up shows. Without it a channel could only ever pay off at the end, which
        /// is half of what makes the cost interesting (§5.5.4).
        /// </remarks>
        IReadOnlyList<IEffectStep> StartSteps { get; }

        /// <summary>
        /// What the ability does when it resolves, in order (§8.2). An authored list rather than a method,
        /// which is the whole reason augments can reach inside an ability without knowing whose it is.
        /// </summary>
        IReadOnlyList<IEffectStep> Steps { get; }
    }

    /// <summary>
    /// One champion worth of abilities: five of them, one per role (§5.5.3).
    /// </summary>
    /// <remarks>
    /// The fixed layout is a design convention over an indexed array, not a structure — so this holds
    /// a list and <see cref="AbilityValidation"/> is what insists the list is five abilities with one
    /// of each role. That is what keeps "your third ability is replaced" a one-line change (§8.3).
    /// </remarks>
    public interface IChampionDefinition : IContentDefinition
    {
        string DisplayName { get; }

        /// <summary>The abilities, in slot order.</summary>
        IReadOnlyList<IAbilityDefinition> Abilities { get; }
    }
}
