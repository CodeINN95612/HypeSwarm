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
    ///
    /// <para><b>Held and latched are deliberate per ability, not a uniform choice.</b> The primary is
    /// read as held, because it is the ability the player uses continuously and making them tap it at
    /// the cooldown is busywork rather than skill. Everything else is latched, because a secondary or
    /// an ultimate fired by a key the player was still holding is a cooldown spent on nothing.</para>
    /// </remarks>
    public sealed class GameplayInput : IDisposable
    {
        public const string MapName = "Gameplay";
        public const string MoveActionName = "Move";
        public const string AimActionName = "Aim";
        public const string DashActionName = "Dash";
        public const string PrimaryActionName = "Primary";
        public const string SecondaryActionName = "Secondary";
        public const string UltimateActionName = "Ultimate";

        /// <summary>Every action this class requires. The asset test iterates it.</summary>
        public static readonly string[] RequiredActions =
        {
            MoveActionName,
            AimActionName,
            DashActionName,
            PrimaryActionName,
            SecondaryActionName,
            UltimateActionName
        };

        readonly InputActionMap map;
        readonly InputAction move;
        readonly InputAction aim;
        readonly InputAction dash;
        readonly InputAction primary;
        readonly InputAction secondary;
        readonly InputAction ultimate;

        bool dashLatched;
        bool secondaryLatched;
        bool ultimateLatched;

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
            primary = map.FindAction(PrimaryActionName, throwIfNotFound: true);
            secondary = map.FindAction(SecondaryActionName, throwIfNotFound: true);
            ultimate = map.FindAction(UltimateActionName, throwIfNotFound: true);

            dash.performed += OnDashPerformed;
            secondary.performed += OnSecondaryPerformed;
            ultimate.performed += OnUltimatePerformed;
        }

        /// <summary>Raw planar movement intent, before the camera's yaw is applied.</summary>
        public Vector2 Move => move.ReadValue<Vector2>();

        /// <summary>Pointer position in screen pixels.</summary>
        public Vector2 AimScreenPosition => aim.ReadValue<Vector2>();

        /// <summary>
        /// Whether the primary is being held. Read every frame rather than latched, so the ability
        /// fires as often as its cooldown allows for as long as the button is down.
        /// </summary>
        public bool PrimaryHeld => primary.IsPressed();

        /// <summary>
        /// Whether the ultimate button is still down. What a channel reads: letting go is the release,
        /// and the release is the decision (§5.5.4).
        /// </summary>
        public bool UltimateHeld => ultimate.IsPressed();

        public void Enable()
        {
            map.Enable();
        }

        public void Disable()
        {
            map.Disable();
            dashLatched = false;
            secondaryLatched = false;
            ultimateLatched = false;
        }

        /// <summary>
        /// True once per press. Reading clears the latch, so exactly one caller may consume it —
        /// which is the point: two consumers would each see the press and spend a charge.
        /// </summary>
        public bool ConsumeDashPressed() => Consume(ref dashLatched);

        /// <summary>True once per press of the secondary.</summary>
        public bool ConsumeSecondaryPressed() => Consume(ref secondaryLatched);

        /// <summary>True once per press of the ultimate. The release is <see cref="UltimateHeld"/>.</summary>
        public bool ConsumeUltimatePressed() => Consume(ref ultimateLatched);

        public void Dispose()
        {
            dash.performed -= OnDashPerformed;
            secondary.performed -= OnSecondaryPerformed;
            ultimate.performed -= OnUltimatePerformed;

            map.Disable();
        }

        static bool Consume(ref bool latch)
        {
            if (!latch)
            {
                return false;
            }

            latch = false;

            return true;
        }

        void OnDashPerformed(InputAction.CallbackContext context)
        {
            dashLatched = true;
        }

        void OnSecondaryPerformed(InputAction.CallbackContext context)
        {
            secondaryLatched = true;
        }

        void OnUltimatePerformed(InputAction.CallbackContext context)
        {
            ultimateLatched = true;
        }
    }
}
