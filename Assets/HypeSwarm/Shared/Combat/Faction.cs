namespace HypeSwarm.Shared.Combat
{
    /// <summary>Which side something is on.</summary>
    /// <remarks>
    /// Three values, because that is all a co-op horde game needs: the party, the horde, and things
    /// that belong to neither. There is no PvP, so there is no team number and no per-player
    /// allegiance — the party is one faction and friendly fire does not exist.
    ///
    /// <para>Numbers are frozen: this crosses the wire on every combatant.</para>
    /// </remarks>
    public enum Faction
    {
        /// <summary>The party. Every champion and anything they summon.</summary>
        Players = 0,

        /// <summary>The horde. Trash, elites, bosses.</summary>
        Enemies = 1,

        /// <summary>
        /// Hostile to nobody and targetable by nobody — destructibles, training dummies that are
        /// meant to be hit only deliberately, siege structures before they are claimed.
        /// </summary>
        Neutral = 2
    }

    /// <summary>Who may hit whom.</summary>
    public static class Factions
    {
        /// <summary>
        /// Whether <paramref name="other"/> is a valid hostile target for <paramref name="self"/>.
        /// </summary>
        /// <remarks>
        /// Neutral is hostile to nothing <i>and</i> nothing is hostile to it, which is what makes it
        /// useful: a destructible in the middle of a fight must not eat a cleave meant for the horde,
        /// and a zone dropped on one must not tick on it forever.
        /// </remarks>
        public static bool AreHostile(Faction self, Faction other)
        {
            if (self == Faction.Neutral || other == Faction.Neutral)
            {
                return false;
            }

            return self != other;
        }

        /// <summary>Whether the two are on the same side. A faction is always allied to itself.</summary>
        public static bool AreAllied(Faction self, Faction other) => self == other;
    }
}
