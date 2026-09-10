using HypeSwarm.Shared.Content;

namespace HypeSwarm.Shared.Abilities.Steps
{
    /// <summary>
    /// Base class for authored effect steps: a <see cref="ContentDefinition"/> that does something.
    /// </summary>
    /// <remarks>
    /// A <see cref="ContentDefinition"/> rather than a bare ScriptableObject, so every step has a
    /// stable id (§13.2). That matters sooner than it looks: the trace tool at Phase 3 step 10 reports
    /// a cast step by step, and it has to name them in a way that survives renaming the asset.
    ///
    /// <para>The defaults are the safe ones — runs on the host, does not run on the casting client,
    /// needs nothing and provides nothing. A step that changes the world inherits all four and says
    /// nothing; only a step that moves the player, or one that fills the target list, has to opt in.
    /// Getting that backwards would mean a client that applies its own damage.</para>
    /// </remarks>
    public abstract class EffectStep : ContentDefinition, IEffectStep
    {
        /// <inheritdoc />
        public virtual bool RunsOnAuthority => true;

        /// <inheritdoc />
        public virtual bool RunsOnPrediction => false;

        /// <inheritdoc />
        public virtual bool NeedsTargets => false;

        /// <inheritdoc />
        public virtual bool ProvidesTargets => false;

        /// <inheritdoc />
        public abstract void Execute(AbilityContext context);
    }
}
