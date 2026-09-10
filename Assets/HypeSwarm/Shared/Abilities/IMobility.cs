using UnityEngine;

namespace HypeSwarm.Shared.Abilities
{
    /// <summary>
    /// Something an ability can move. Implemented by whatever owns the motor on this machine.
    /// </summary>
    /// <remarks>
    /// <b>Null on every machine but the one that owns the champion.</b> Movement is
    /// client-authoritative (§10): the owner moves itself and everyone else watches the replicated
    /// transform, so a dash step on the host must not push a remote champion around. The step asks the
    /// context for this and does nothing when it is absent, which is exactly the behaviour wanted on
    /// the host and on the other four clients.
    ///
    /// <para>An interface so the ability system never sees <c>CharacterMotor</c> or a
    /// <c>CharacterController</c>. A summon, an elite, and the headless simulator all move differently
    /// and a dash step should not know which one it is talking to.</para>
    /// </remarks>
    public interface IMobility
    {
        /// <summary>
        /// Starts a dash.
        /// </summary>
        /// <param name="direction">
        /// Planar direction, or zero to let the implementation choose — which means the direction of
        /// travel, falling back to facing. Zero is the usual case: with aim on the cursor, a dash that
        /// followed aim could only ever go toward what you are shooting, and the escape half of the
        /// ability would be unreachable.
        /// </param>
        /// <returns>False when a dash is already running.</returns>
        bool TryDash(Vector2 direction);
    }
}
