using HypeSwarm.Shared.Stats;
using UnityEngine;

namespace HypeSwarm.Shared.Combat
{
    /// <summary>
    /// Anything an ability can find, aim at, and affect: a champion, a trash mob, an elite, a
    /// destructible.
    /// </summary>
    /// <remarks>
    /// <see cref="IDamageable"/> is what the damage pipeline needs and nothing more. This is what an
    /// <i>effect step</i> needs — where the thing is, whose side it is on, what its stats are, and the
    /// handful of things an effect does to it besides damage. Kept as an interface for the same reason
    /// <see cref="Content.IContentDefinition"/> is: effect steps are then testable against a plain C#
    /// double with no scene, no prefab, and no network.
    ///
    /// <para>Everything here that changes state is host-authoritative (§10). Effect steps call these
    /// only when the context says they are running with authority; on a client the same steps run for
    /// presentation and touch nothing.</para>
    /// </remarks>
    public interface ICombatant : IDamageable
    {
        /// <summary>
        /// Network id, for the <see cref="DamagePacket"/> this entity sends. Zero when unspawned,
        /// which the pipeline already treats as "no attacker".
        /// </summary>
        uint Id { get; }

        /// <summary>Which side this is on.</summary>
        Faction Faction { get; }

        /// <summary>World position. What targeting measures distance against.</summary>
        Vector3 Position { get; }

        /// <summary>
        /// The stats abilities scale from. Never null for anything that can cast; may be a sheet of
        /// pure defaults for something that only takes damage.
        /// </summary>
        StatSheet Stats { get; }

        /// <summary>Restores health, returning what actually landed. Server only.</summary>
        float Heal(float amount);

        /// <summary>Puts absorption in front of health, replacing whatever that source had. Server only.</summary>
        bool AddShield(ModifierSource source, float amount, float duration);

        /// <summary>
        /// Applies timed stat modifiers — a slow, a shred, a haste buff. Server only.
        /// </summary>
        /// <remarks>
        /// Returns false when this entity has no replicated stat sheet to put them on, which is the
        /// honest answer for a destructible and is how a slow step knows it did nothing. Modifiers
        /// rather than a bespoke status type, because a slow <i>is</i> negative move speed and a
        /// shred <i>is</i> negative damage reduction — giving them their own system would mean two
        /// places where a number comes from (§5.6.5).
        /// </remarks>
        bool ApplyModifiers(ModifierSource source, float duration, StatModifier[] modifiers);
    }
}
