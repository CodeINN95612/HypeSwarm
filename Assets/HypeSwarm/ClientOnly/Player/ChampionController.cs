using HypeSwarm.ClientOnly.Controls;
using HypeSwarm.Shared.Abilities;
using HypeSwarm.Shared.Movement;
using HypeSwarm.Shared.Net;
using HypeSwarm.Shared.Stats;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HypeSwarm.ClientOnly.Player
{
    /// <summary>
    /// Drives one champion from local input. The client half of movement: devices, a camera, a
    /// collider, and a transform.
    /// </summary>
    /// <remarks>
    /// All of the movement rules live in <see cref="CharacterMotor"/> in <c>Shared</c>. What remains
    /// here is genuinely client work — turning a keyboard into a direction, a cursor into an aim
    /// vector, and a displacement into a <c>CharacterController.Move</c> call. That division is what
    /// let movement be networked (§10) without touching a rule: the champion five other machines see
    /// is driven by <see cref="ChampionNetworkState"/> and never by this file.
    ///
    /// <para>This runs on the owning client only, and ships <b>disabled on the prefab</b>;
    /// <see cref="ChampionOwnership"/> enables it once authority arrives. Movement is
    /// client-authoritative (§10), so this is the only machine that decides where this champion
    /// goes.</para>
    ///
    /// <para>Movement is stepped in <c>Update</c>, not <c>FixedUpdate</c>. There is no rigidbody
    /// here and none is planned (§11), so a fixed step would only add a frame of latency to the
    /// thing the player is most sensitive to.</para>
    /// </remarks>
    [RequireComponent(typeof(CharacterController))]
    public sealed class ChampionController : MonoBehaviour, IMobility
    {
        [Header("Bindings")]
        [SerializeField]
        [Tooltip("The HypeSwarmControls asset. Must contain a Gameplay map with Move, Aim, and Dash.")]
        InputActionAsset controls;

        [SerializeField]
        [Tooltip("Camera used to turn the cursor into a world position. Falls back to Camera.main.")]
        Camera aimCamera;

        [Header("Presentation")]
        [SerializeField]
        [Tooltip("Optional marker placed where the cursor meets the ground.")]
        Transform reticle;

        [Header("Movement")]
        [SerializeField]
        MovementSettings movement = new MovementSettings();

        [Header("Ground")]
        [SerializeField]
        [Tooltip("Downward acceleration. Only enough to hold the character on slopes and stairs — " +
                 "there is no jump, and there is not going to be one.")]
        float gravity = 30f;

        CharacterController body;
        ChampionNetworkState state;
        ChampionStats stats;
        AbilityRunner abilities;
        GameplayInput input;
        CharacterMotor motor;
        Vector2 lastMove;
        float verticalSpeed;

        /// <summary>
        /// The simulation. Exposed so presentation and HUD can subscribe to its events.
        /// </summary>
        /// <remarks>
        /// Built on first access rather than in <c>Awake</c>. Unity does not order <c>Awake</c>
        /// between GameObjects, so a presentation component on a child that subscribed in its own
        /// <c>OnEnable</c> would sometimes find no motor and sometimes find one — the kind of bug
        /// that reproduces on one machine in five.
        /// </remarks>
        public CharacterMotor Motor => motor ?? (motor = new CharacterMotor(movement));

        /// <summary>Where the cursor currently meets the ground, in world space.</summary>
        public Vector3 AimPoint { get; private set; }

        /// <summary>True while the cursor resolves to a real ground position.</summary>
        public bool HasAimPoint { get; private set; }

        /// <summary>
        /// Points this champion at the camera that owns it. Called by <see cref="ChampionOwnership"/>
        /// when authority arrives, because a spawned prefab cannot carry a scene reference.
        /// </summary>
        public void AssignCamera(Camera camera)
        {
            if (camera != null)
            {
                aimCamera = camera;
            }
        }

        void Awake()
        {
            body = GetComponent<CharacterController>();
            state = GetComponent<ChampionNetworkState>();
            stats = GetComponent<ChampionStats>();
            abilities = GetComponent<AbilityRunner>();
            Motor.Reset(MotionPlane.Flatten(transform.forward));
            AimPoint = transform.position;

            if (controls != null)
            {
                input = new GameplayInput(controls);
            }
            else
            {
                Debug.LogError(
                    $"{name} has no controls asset assigned, so it will not respond to input.", this);
            }
        }

        void OnEnable()
        {
            input?.Enable();

            // Subscribed here rather than in Awake so a champion that never gains authority never
            // announces a dash it did not take.
            Motor.DashStarted += OnDashStarted;
            Motor.DashEnded += OnDashEnded;
        }

        void OnDisable()
        {
            input?.Disable();

            Motor.DashStarted -= OnDashStarted;
            Motor.DashEnded -= OnDashEnded;
        }

        /// <summary>
        /// Feeds the move speed stat and the cost of whatever is being cast into the motor.
        /// </summary>
        /// <remarks>
        /// The stat stores linear and derives asymptotic (§5.6.2), so <c>Effect</c> is the share of the
        /// way to the asymptote and the bonus tops out just short of doubling the champion. That bound
        /// is the point rather than a limitation — unbounded movement breaks a horde game, because with
        /// no basic attacks speed converts into effective health more directly than anything else does.
        ///
        /// <para>Read every frame rather than on a change event. It is one cached array lookup and one
        /// divide, and a stat that only arrives when something remembers to push it is a stat that is
        /// wrong after the first code path that forgets.</para>
        ///
        /// <para><b>The two multiply</b>, which is the whole reason the motor takes one number (§5.5.4).
        /// A rooted cast is a multiplier of zero, so the fastest champion in the game still stands
        /// perfectly still while casting one — and nothing had to decide whether a root beats a haste.</para>
        /// </remarks>
        void ApplyMoveSpeed()
        {
            var fromStats = stats == null ? 1f : 1f + stats.Sheet.Effect(StatId.MoveSpeed);
            var fromCasting = abilities == null ? 1f : abilities.MovementMultiplier;

            Motor.SpeedMultiplier = fromStats * fromCasting;
        }

        /// <summary>
        /// Where this champion is currently aiming, in the form an ability wants.
        /// </summary>
        /// <remarks>
        /// Falls back to facing when the cursor does not resolve to the ground — off the edge of the
        /// arena, or past the horizon. Refusing to cast would be worse: the player pressed the button,
        /// and a cast that goes where the champion is looking is a reasonable reading of that.
        /// </remarks>
        AbilityAim CurrentAim()
        {
            return HasAimPoint
                ? AbilityAim.FromPoint(transform.position, AimPoint, Motor.Facing)
                : AbilityAim.FromDirection(transform.position, Motor.Facing);
        }

        /// <summary>
        /// Turns this frame's input into casts.
        /// </summary>
        /// <remarks>
        /// <b>The dash key casts the mobility ability</b> rather than reaching the motor directly, once
        /// the champion has one (§5.5.3). That is what makes the slot an ability: its cooldown, its
        /// charges and the damage it leaves behind are all authored, and augments can reach it. A
        /// champion with no mobility ability authored still dashes, through the motor's own charges,
        /// which is what keeps the movement prototype usable on its own.
        /// </remarks>
        /// <returns>Whether the dash press still belongs to the motor.</returns>
        bool StepAbilities(bool dashPressed)
        {
            if (input == null || abilities == null || abilities.Book == null)
            {
                return dashPressed;
            }

            var aim = CurrentAim();

            if (dashPressed && abilities.Book.IndexOf(AbilityRole.Mobility) >= 0)
            {
                abilities.RequestCast(AbilityRole.Mobility, aim);
                dashPressed = false;
            }

            // Held, not latched: the primary is the ability the player uses continuously, and it fires
            // as often as its cooldown allows for as long as the button is down.
            if (input.PrimaryHeld)
            {
                abilities.RequestCast(AbilityRole.Primary, aim);
            }

            if (input.ConsumeSecondaryPressed())
            {
                abilities.RequestCast(AbilityRole.Secondary, aim);
            }

            // The press starts the ultimate and letting go resolves it, which for a channel is the
            // decision the player is making. For an ultimate that does not channel the release does
            // nothing, so one pair of lines covers both.
            if (input.ConsumeUltimatePressed())
            {
                abilities.RequestCast(AbilityRole.Ultimate, aim);
            }
            else if (!input.UltimateHeld && abilities.Book.IsChannelling)
            {
                abilities.ReleaseChannel();
            }

            return dashPressed;
        }

        /// <summary>
        /// Dashes, for the mobility ability's dash step. The one thing the ability system asks of the
        /// movement side (§10: this machine owns where this champion goes).
        /// </summary>
        public bool TryDash(Vector2 direction)
        {
            return Motor.TryDash(direction.sqrMagnitude > 0f ? direction : lastMove);
        }

        void OnDashStarted(DashEvent dash) => state?.SubmitDashStarted(dash.Duration);

        void OnDashEnded() => state?.SubmitDashEnded();

        void OnDestroy()
        {
            input?.Dispose();
        }

        void OnValidate()
        {
            movement?.Validate();

            if (motor != null)
            {
                motor.Settings = movement;
            }
        }

        void Update()
        {
            var deltaTime = Time.deltaTime;
            var camera = ResolveCamera();

            var aim = UpdateAim(camera);
            var move = input == null
                ? Vector2.zero
                : AimGeometry.CameraRelative(input.Move, camera == null ? 0f : camera.transform.eulerAngles.y);

            lastMove = move;

            // Abilities before movement: a cast that roots has to stop this frame rather than the next
            // one, and a dash has to be part of this frame's displacement or it reads as a dropped input.
            var dashPressed = StepAbilities(input != null && input.ConsumeDashPressed());

            ApplyMoveSpeed();

            var motionInput = new MovementInput(move, aim, dashPressed);
            var displacement = Motor.Step(motionInput, deltaTime);

            ApplyMotion(displacement, deltaTime);

            // Written, not applied. ChampionFacingPresenter turns the visual from this value on
            // every machine, so the champion the other four players see cannot drift from ours.
            state?.SubmitFacing(Motor.Facing);

            // After the move, not before. The reticle hangs off the champion, so placing it first
            // means the body then drags it along by exactly this frame's displacement — a lag that
            // grows with speed and is at its worst mid-dash, which is when aim matters most.
            PlaceReticle();
        }

        Camera ResolveCamera()
        {
            if (aimCamera == null)
            {
                aimCamera = Camera.main;
            }

            return aimCamera;
        }

        /// <summary>
        /// Resolves the cursor to a world-plane aim direction, returning <see cref="Vector2.zero"/>
        /// — "no opinion" — when it cannot. Holding the last facing is the right failure: snapping
        /// to a default direction because the cursor crossed the horizon is worse than not turning.
        /// </summary>
        Vector2 UpdateAim(Camera camera)
        {
            HasAimPoint = false;

            if (camera == null || input == null)
            {
                return Vector2.zero;
            }

            var screenPosition = input.AimScreenPosition;

            if (!AimGeometry.TryGroundPoint(
                    camera.ScreenPointToRay(screenPosition), transform.position.y, out var point))
            {
                return Vector2.zero;
            }

            AimPoint = point;
            HasAimPoint = true;

            return MotionPlane.DirectionBetween(transform.position, point);
        }

        void ApplyMotion(Vector2 displacement, float deltaTime)
        {
            verticalSpeed = body.isGrounded ? -2f : verticalSpeed - gravity * deltaTime;

            var motion = MotionPlane.ToWorld(displacement);
            motion.y = verticalSpeed * deltaTime;

            var collisions = body.Move(motion);

            // A dash that has run into a wall should end, not spend its remaining duration pressing
            // into geometry. Without this the character sticks to the wall for the rest of the dash
            // and the ability reads as having been eaten.
            if (Motor.IsDashing && (collisions & CollisionFlags.Sides) != 0)
            {
                Motor.CancelDash();
            }
        }

        void PlaceReticle()
        {
            if (reticle != null && HasAimPoint)
            {
                reticle.position = AimPoint;
            }
        }
    }
}
