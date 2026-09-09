using UnityEngine;

namespace HypeSwarm.Shared.Movement
{
    /// <summary>
    /// One frame of intent, already resolved into world-plane terms.
    /// </summary>
    /// <remarks>
    /// Deliberately free of devices, cameras, and screen coordinates. Turning a key press into a
    /// direction and a cursor position into an aim vector is the client's job (§13.1); by the time
    /// intent reaches the motor it is two vectors and a flag, which is also the shape that will
    /// travel over the wire when movement becomes networked (§10).
    /// </remarks>
    public readonly struct MovementInput
    {
        /// <summary>
        /// Desired direction of travel on the plane. Magnitudes above one are clamped by the motor,
        /// so an analog stick keeps its partial deflection while diagonal keyboard input does not
        /// travel faster than cardinal input.
        /// </summary>
        public readonly Vector2 Move;

        /// <summary>
        /// Direction the character wants to face. <see cref="Vector2.zero"/> means "no opinion" and
        /// leaves facing where it is, which is what a cursor sitting exactly on the character
        /// should do.
        /// </summary>
        public readonly Vector2 Aim;

        /// <summary>
        /// True on the frame the dash was pressed, not while it is held. A held flag would spend
        /// every charge the moment the lockout expired.
        /// </summary>
        public readonly bool DashPressed;

        public MovementInput(Vector2 move, Vector2 aim, bool dashPressed = false)
        {
            Move = move;
            Aim = aim;
            DashPressed = dashPressed;
        }

        public static MovementInput None => new MovementInput(Vector2.zero, Vector2.zero);

        /// <summary>Movement with no aim change and no dash. Convenience for tests and AI.</summary>
        public static MovementInput Moving(Vector2 move) => new MovementInput(move, Vector2.zero);
    }
}
