namespace HypeSwarm.Shared.Stats
{
    /// <summary>
    /// Every stat in the game. One damage stat, one damage reduction stat, no schools (spec §5.6.1).
    /// </summary>
    /// <remarks>
    /// <b>These numbers are frozen.</b> They index arrays in <see cref="StatSheet"/>, they cross the
    /// wire inside <see cref="StatModifier"/>, and they will end up in save files — so a stat is
    /// added at the end and never renumbered, and a retired one leaves its number behind.
    ///
    /// <para>There is deliberately no area and no range stat (§5.5.5), and no attack speed, because
    /// there are no basic attacks (§5.5.2). Projectile count is not here either: it is a discrete
    /// quantity derived from other stats through an authored step function (§5.6.5), not something
    /// a modifier adds a fraction of.</para>
    ///
    /// <para>Stats carry no tags and no behaviour. They are numbers; abilities read them (§5.6.5).
    /// Anything that wants to change a <i>rule</i> is an augment, not a stat.</para>
    /// </remarks>
    public enum StatId
    {
        /// <summary>Health pool. Linear and unbounded.</summary>
        MaxHealth = 0,

        /// <summary>Health restored per second out of combat pressure. Linear.</summary>
        HealthRegen = 1,

        /// <summary>The one damage stat. Linear.</summary>
        Damage = 2,

        /// <summary>Reduces the target's damage reduction before it is derived. Linear.</summary>
        Penetration = 3,

        /// <summary>Incoming damage removed. Stored linear, derived asymptotic (§5.6.2).</summary>
        DamageReduction = 4,

        /// <summary>Cooldown speed, League-style. Stored linear, derived asymptotic.</summary>
        Haste = 5,

        /// <summary>Movement bonus. Stored linear, derived asymptotic — unbounded movement breaks a
        /// horde game (§5.6.2).</summary>
        MoveSpeed = 6,

        /// <summary>Chance to avoid a hit outright. Stored linear, derived asymptotic.</summary>
        Dodge = 7,

        /// <summary>Damage dealt returned as health. Stored linear, derived asymptotic.</summary>
        Lifesteal = 8,

        /// <summary>How much of an applied slow is ignored. Stored linear, derived asymptotic.</summary>
        SlowResistance = 9,

        /// <summary>Radius in which drops are collected. Linear.</summary>
        PickupRadius = 10
    }

    /// <summary>Facts about <see cref="StatId"/> that the enum itself cannot express.</summary>
    public static class StatIds
    {
        /// <summary>
        /// How many stats there are. <see cref="StatSheet"/> sizes its arrays with this and indexes
        /// them by the raw enum value, so the values must stay a gapless run from zero — there is a
        /// test pinning exactly that.
        /// </summary>
        public const int Count = 11;

        /// <summary>Every stat, in declaration order.</summary>
        public static readonly StatId[] All =
        {
            StatId.MaxHealth,
            StatId.HealthRegen,
            StatId.Damage,
            StatId.Penetration,
            StatId.DamageReduction,
            StatId.Haste,
            StatId.MoveSpeed,
            StatId.Dodge,
            StatId.Lifesteal,
            StatId.SlowResistance,
            StatId.PickupRadius
        };

        /// <summary>
        /// Whether this is a stat that exists. Worth asking about anything that arrived over the
        /// network or out of a save file: a host on a newer build can name a stat this one has never
        /// heard of, and the sheet indexes arrays by the raw value.
        /// </summary>
        public static bool IsDefined(StatId stat) => (int)stat >= 0 && (int)stat < Count;
    }
}
