using UnityEngine;

namespace HypeSwarm.Shared.Abilities.Steps
{
    /// <summary>
    /// Dashes the caster. The movement half of a mobility ability (§5.5.3).
    /// </summary>
    /// <remarks>
    /// <b>This runs on the casting client first and is not waited on.</b> Mobility is the one thing in
    /// the game that must be instant — it is the primary survival tool, and a dash that arrives a round
    /// trip after the key reads as the game dropping the input. Movement is client-authoritative (§10),
    /// so the client moving itself is not a prediction that can be wrong; it is the decision.
    ///
    /// <para>On every other machine <see cref="AbilityContext.Mobility"/> is null and this does nothing,
    /// which is what must happen: the host pushing a remote champion around would fight the replicated
    /// transform, and a client running this for somebody else's cast would teleport a champion it does
    /// not own.</para>
    ///
    /// <para><b>How far a dash goes is not on this step.</b> Distance and duration belong to the motor,
    /// because they are movement feel rather than ability data, and because a dash that covered a
    /// different distance per ability would make mobility impossible to read at a glance (§5.5.5).</para>
    /// </remarks>
    [CreateAssetMenu(menuName = "Hype Swarm/Effect Steps/Dash", fileName = "Dash")]
    public sealed class DashStep : EffectStep
    {
        [SerializeField]
        [Tooltip("Dash toward the cursor instead of the direction of travel.\n\n" +
                 "Off by default, and the default is the considered one: with aim on the cursor, a dash " +
                 "that followed aim could only ever go toward what you are shooting, and the escape " +
                 "half of the ability would be unreachable.")]
        bool towardAim;

        /// <summary>
        /// The client moves itself immediately. This is the step the whole prediction distinction exists
        /// for.
        /// </summary>
        public override bool RunsOnPrediction => true;

        public override void Execute(AbilityContext context)
        {
            if (context?.Mobility == null)
            {
                return;
            }

            // Zero means "your own default direction", which the motor resolves as travel then facing.
            var direction = towardAim && context.Aim.HasDirection ? context.Aim.Direction : Vector2.zero;

            context.Mobility.TryDash(direction);
        }
    }
}
