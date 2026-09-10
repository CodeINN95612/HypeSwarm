namespace HypeSwarm.Shared.Abilities
{
    /// <summary>
    /// One thing an ability does: deal damage, apply a shield, select targets, dash (§8.2).
    /// </summary>
    /// <remarks>
    /// <b>Abilities are lists of these rather than methods</b>, and that is not a stylistic choice.
    /// An augment has to reach inside an ability and modify what it does without knowing which
    /// champion it belongs to; a method called <c>OnQ</c> has no seams to reach into, and a list of
    /// small objects does (§8).
    ///
    /// <para>Target roughly 30 to 60 meaningful step types, and stop there. Atomising further into a
    /// node graph with arithmetic nodes makes authoring worse than writing C#, which defeats the point
    /// of authoring at all.</para>
    ///
    /// <para><b>No step instantiates a particle system or plays a sound.</b> Steps emit events;
    /// presentation subscribes (§12). The asmdef boundary enforces it — <c>Shared</c> cannot see
    /// <c>UnityEngine.UI</c>, particle systems, or <c>AudioSource</c>.</para>
    /// </remarks>
    public interface IEffectStep
    {
        /// <summary>
        /// Whether this runs on the machine that owns the outcome. Almost always true: damage,
        /// shields, buffs and kills are the host's to decide (§10).
        /// </summary>
        bool RunsOnAuthority { get; }

        /// <summary>
        /// Whether this also runs immediately on the casting client, before the host has confirmed
        /// anything.
        /// </summary>
        /// <remarks>
        /// True for what the player must feel at once — the dash, the animation, the cooldown — and
        /// false for anything that changes the world. A client that applied its own damage would be a
        /// client that decides what dies (§5.5.1).
        /// </remarks>
        bool RunsOnPrediction { get; }

        /// <summary>
        /// Whether this step reads <see cref="AbilityContext.Targets"/>. Validation uses it to catch
        /// the commonest authoring mistake: an ability that damages a target list nothing filled.
        /// </summary>
        bool NeedsTargets { get; }

        /// <summary>Whether this step is what fills <see cref="AbilityContext.Targets"/>.</summary>
        bool ProvidesTargets { get; }

        /// <summary>Does the thing. Everything it needs is on the context.</summary>
        void Execute(AbilityContext context);
    }
}
