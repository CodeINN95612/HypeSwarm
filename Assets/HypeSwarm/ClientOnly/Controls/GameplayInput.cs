using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HypeSwarm.ClientOnly.Controls
{
    /// <summary>
    /// The gameplay action map, resolved once and read as plain values.
    /// </summary>
    /// <remarks>
    /// Actions are looked up by name rather than through a generated wrapper class. The wrapper is
    /// a build artefact regenerated on import, which means it is either committed and goes stale or
    /// is not committed and the project does not compile from a clean clone. Names are the cost of
    /// avoiding that, and <c>GameplayInputTests</c> asserts the shipped asset actually contains the
    /// ones this class asks for — so a renamed action fails a test rather than failing silently at
    /// runtime with a character that will not move.
    ///
    /// <para>Dash is latched rather than polled. A press that lands between two reads would
    /// otherwise be dropped, and a dropped dash in a horde game is a death the player will
    /// correctly blame on the game.</para>
    /// </remarks>
    public sealed class GameplayInput : IDisposable
    {
        public const string MapName = "Gameplay";
        public const string MoveActionName = "Move";
        public const string AimActionName = "Aim";
        public const string DashActionName = "Dash";

        /// <summary>Every action this class requires. The asset test iterates it.</summary>
        public static readonly string[] RequiredActions = { MoveActionName, AimActionName, DashActionName };

        readonly InputActionMap map;
        readonly InputAction move;
        readonly InputAction aim;
        readonly InputAction dash;

        bool dashLatched;

        public GameplayInput(InputActionAsset asset)
        {
            if (asset == null)
            {
                throw new ArgumentNullException(nameof(asset));
            }

            map = asset.FindActionMap(MapName, throwIfNotFound: true);
            move = map.FindAction(MoveActionName, throwIfNotFound: true);
            aim = map.FindAction(AimActionName, throwIfNotFound: true);
            dash = map.FindAction(DashActionName, throwIfNotFound: true);

            dash.performed += OnDashPerformed;
        }

        /// <summary>Raw planar movement intent, before the camera's yaw is applied.</summary>
        public Vector2 Move => move.ReadValue<Vector2>();

        /// <summary>Pointer position in screen pixels.</summary>
        public Vector2 AimScreenPosition => aim.ReadValue<Vector2>();

        public void Enable()
        {
            map.Enable();
        }

        public void Disable()
        {
            map.Disable();
            dashLatched = false;
        }

        /// <summary>
        /// True once per press. Reading clears the latch, so exactly one caller may consume it —
        /// which is the point: two consumers would each see the press and spend a charge.
        /// </summary>
        public bool ConsumeDashPressed()
        {
            if (!dashLatched)
            {
                return false;
            }

            dashLatched = false;
            return true;
        }

        public void Dispose()
        {
            dash.performed -= OnDashPerformed;
            map.Disable();
        }

        void OnDashPerformed(InputAction.CallbackContext context)
        {
            dashLatched = true;
        }
    }
}
