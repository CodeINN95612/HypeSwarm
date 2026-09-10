namespace HypeSwarm.Shared.Abilities
{
    /// <summary>
    /// How a cast is aimed. Everything is aimed; nothing is clicked on (§5.5.1).
    /// </summary>
    /// <remarks>
    /// There is deliberately no <c>Unit</c> mode. Click-targeting is the thing this game is not: the
    /// player moves continuously and aims independently, so an ability resolves against a point or a
    /// direction and whatever happens to be standing there. That is also what makes hit registration
    /// matter, and why the client shows its own effects immediately while the host confirms damage.
    ///
    /// <para>Shapes are not here. <i>Where</i> a cast is pointed is a property of the ability; the
    /// cone, circle or line it then fills is a property of the effect step that selects targets
    /// (§8.2), so one ability can have several.</para>
    /// </remarks>
    public enum TargetingMode
    {
        /// <summary>Ignores aim entirely. Self-shields, channels, passives.</summary>
        Self = 0,

        /// <summary>
        /// A ground point, clamped to the ability's authored range. The range clamp happens on the
        /// host as well as the client, because an unclamped point is a client casting across the map.
        /// </summary>
        AimPoint = 1,

        /// <summary>
        /// A direction from the caster, with the distance ignored. Cones, lines and dashes — where
        /// how far the cursor is does not and should not change what the ability does.
        /// </summary>
        AimDirection = 2
    }

    /// <summary>Who an effect is looking for, relative to whoever cast it.</summary>
    /// <remarks>
    /// Relative rather than absolute on purpose. An effect step authored against
    /// <see cref="Enemies"/> works unchanged when an elite casts it at the party (§6.4), which is the
    /// whole reason elites use the champion ability system rather than a parallel one.
    /// </remarks>
    public enum TargetFaction
    {
        /// <summary>Anything hostile to the caster.</summary>
        Enemies = 0,

        /// <summary>The caster's own side, the caster included.</summary>
        Allies = 1,

        /// <summary>The caster and nothing else.</summary>
        Caster = 2
    }
}
