using System;
using HypeSwarm.Shared.Cooldowns;
using UnityEngine;

namespace HypeSwarm.Shared.Movement
{
    /// <summary>
    /// Turns intent into planar displacement. The whole of how a champion moves, with no knowledge
    /// of transforms, colliders, cameras, or input devices.
    /// </summary>
    /// <remarks>
    /// This lives in <c>Shared</c> because three different callers will need it and only one of them
    /// has a screen: the local player predicting their own movement, the host validating what a
    /// client claimed (§10), and the headless balance simulator (Phase 19). It returns a desired
    /// displacement rather than moving anything, so the caller decides whether that means a
    /// <c>CharacterController</c>, a spatial-hash entry, or a number in a spreadsheet.
    ///
    /// <para><b>Facing is independent of travel.</b> That single property is what makes this
    /// Supervive-style rather than League-style (§5.5.1), and it is why aim is a separate vector
    /// instead of being inferred from velocity. It holds during a dash too, so retreating while
    /// still aiming at what is chasing you works — which is most of what kiting will be once
    /// abilities exist (§5.5.4).</para>
    ///
    /// <para>Time is a parameter, never <c>Time.deltaTime</c>. Frame rate must not change where a
    /// character ends up: the netcode will re-simulate these steps at a different rate than they
    /// were produced, and a motor that drifts with the tick makes reconciliation impossible.</para>
    /// </remarks>
    public sealed class CharacterMotor
    {
        MovementSettings settings;
        Vector2 dashVelocity;
        float dashTimeRemaining;
        float speedMultiplier = 1f;

        public CharacterMotor(MovementSettings settings = null)
        {
            this.settings = settings ?? new MovementSettings();
            Charges = new ChargePool(
                this.settings.DashCharges,
                this.settings.DashCooldown,
                this.settings.DashLockout);
            Facing = Vector2.up;
        }

        /// <summary>
        /// Live settings. Held by reference on purpose so inspector edits during play are felt on
        /// the next step without a reconfigure call.
        /// </summary>
        public MovementSettings Settings
        {
            get => settings;
            set => settings = value ?? new MovementSettings();
        }

        public ChargePool Charges { get; }

        /// <summary>
        /// Top speed multiplier, from the move speed stat and later from slows. One is unmodified.
        /// </summary>
        /// <remarks>
        /// <b>A multiplier set from outside rather than a stat sheet read from inside.</b> The motor
        /// knowing about stats would be the dependency running the wrong way (§5.6.5) and would put a
        /// <c>StatSheet</c> in the way of every test and of the headless simulator. The caller resolves
        /// <c>Effect(StatId.MoveSpeed)</c> and hands over a number.
        ///
        /// <para>This is also where slows will land, multiplying it down, with slow resistance reducing
        /// how far down. One seam for both, so nothing has to decide whether a slow beats a haste.</para>
        /// </remarks>
        public float SpeedMultiplier
        {
            get => speedMultiplier;
            set => speedMultiplier = value > 0f ? value : 0f;
        }

        /// <summary>
        /// Top speed this step, with the multiplier applied.
        /// </summary>
        /// <remarks>
        /// <b>Dash speed deliberately does not use this.</b> A dash covers an authored distance in an
        /// authored time (§5.5.5) — stats change how hard things hit, never how far they travel — so a
        /// move speed build dashes exactly as far as everyone else. Without that, move speed would
        /// quietly be the best mobility stat in the game as well as the best survival one.
        /// </remarks>
        public float CurrentMaxSpeed => settings.MaxSpeed * speedMultiplier;

        /// <summary>Current planar velocity, in metres per second.</summary>
        public Vector2 Velocity { get; private set; }

        /// <summary>Planar unit vector the character is facing. Never zero.</summary>
        public Vector2 Facing { get; private set; }

        public bool IsDashing => dashTimeRemaining > 0f;

        /// <summary>Fraction of the current dash already travelled, from 0 to 1.</summary>
        public float DashProgress =>
            IsDashing ? 1f - Mathf.Clamp01(dashTimeRemaining / settings.DashDuration) : 0f;

        /// <summary>Raised when a dash begins. Presentation subscribes; the motor does not wait.</summary>
        public event Action<DashEvent> DashStarted;

        /// <summary>Raised when a dash finishes, including when it is cut short by a wall.</summary>
        public event Action DashEnded;

        /// <summary>
        /// Advances one step and returns the planar displacement to apply.
        /// </summary>
        /// <remarks>
        /// A step that spans the end of a dash is split: the remainder of the dash is travelled at
        /// dash speed, and whatever time is left over is travelled under normal control. Without
        /// that split a dash would cover slightly more or less ground depending on where frame
        /// boundaries happened to fall, and a 6-metre dash would be 6 metres only on average.
        /// </remarks>
        public Vector2 Step(in MovementInput input, float deltaTime)
        {
            settings.Validate();

            if (float.IsNaN(deltaTime) || deltaTime <= 0f)
            {
                return Vector2.zero;
            }

            Charges.Configure(settings.DashCharges, settings.DashCooldown, settings.DashLockout);
            Charges.Tick(deltaTime);

            UpdateFacing(input.Aim, deltaTime);

            if (input.DashPressed && !IsDashing && Charges.TrySpend())
            {
                StartDash(input.Move);
            }

            var displacement = Vector2.zero;
            var remaining = deltaTime;

            if (IsDashing)
            {
                displacement += StepDash(ref remaining);
            }

            if (remaining > 0f)
            {
                displacement += StepGround(input.Move, remaining);
            }

            return displacement;
        }

        /// <summary>
        /// Starts a dash now, without asking the charge pool.
        /// </summary>
        /// <remarks>
        /// <b>This is how the mobility ability dashes</b> (§5.5.3). The gate lives on the ability —
        /// charges, cooldown and lockout are all on its <c>AbilityInstance</c>, because an augment that
        /// changes them should change the ability rather than the motor, and because the cooldown the
        /// HUD draws has to be the one the cast actually checked.
        ///
        /// <para>The <see cref="Charges"/> pool below is the other path: <see cref="Step"/> still spends
        /// it for <see cref="MovementInput.DashPressed"/>, which is what the headless simulator and the
        /// motor's own tests use, and what a champion with no authored mobility ability falls back to.
        /// Only one of the two is ever live on a given entity — the controller stops forwarding the dash
        /// input the moment an ability owns the slot — so the two gates cannot disagree.</para>
        /// </remarks>
        /// <param name="direction">
        /// Planar direction, or zero for the motor's own choice: the direction of travel, falling back
        /// to facing.
        /// </param>
        /// <returns>False when a dash is already running.</returns>
        public bool TryDash(Vector2 direction)
        {
            if (IsDashing)
            {
                return false;
            }

            StartDash(direction);

            return true;
        }

        /// <summary>
        /// Stops a dash early, keeping the exit speed. Called when the character hits something —
        /// continuing to push into a wall at dash speed for the rest of the duration is what makes
        /// a dash into a corner feel like it stuck.
        /// </summary>
        public void CancelDash()
        {
            if (!IsDashing)
            {
                return;
            }

            dashTimeRemaining = 0f;
            ApplyDashExitVelocity();
            DashEnded?.Invoke();
        }

        /// <summary>Clears all motion. For respawns, teleports, and test setup.</summary>
        public void Reset(Vector2 facing = default)
        {
            Velocity = Vector2.zero;
            dashVelocity = Vector2.zero;
            dashTimeRemaining = 0f;
            Facing = facing.sqrMagnitude > 0f ? facing.normalized : Vector2.up;
            Charges.Refill();
        }

        void UpdateFacing(Vector2 aim, float deltaTime)
        {
            if (aim.sqrMagnitude <= 0f)
            {
                return;
            }

            var target = aim.normalized;

            if (settings.TurnSpeed <= 0f)
            {
                Facing = target;
                return;
            }

            Facing = RotateTowards(Facing, target, settings.TurnSpeed * Mathf.Deg2Rad * deltaTime);
        }

        void StartDash(Vector2 move)
        {
            // Dashing along the direction of travel rather than the direction of aim: with aim on
            // the cursor, a dash that followed it could only ever go toward what you are shooting,
            // and the escape half of the ability would be unreachable.
            var direction = move.sqrMagnitude > 0f ? move.normalized : Facing;

            dashTimeRemaining = settings.DashDuration;
            dashVelocity = direction * settings.DashSpeed;
            Velocity = dashVelocity;

            DashStarted?.Invoke(new DashEvent(
                direction,
                settings.DashDistance,
                settings.DashDuration,
                Charges.Available));
        }

        Vector2 StepDash(ref float deltaTime)
        {
            var slice = Mathf.Min(dashTimeRemaining, deltaTime);
            var displacement = dashVelocity * slice;

            dashTimeRemaining -= slice;
            deltaTime -= slice;

            if (dashTimeRemaining <= 0f)
            {
                dashTimeRemaining = 0f;
                ApplyDashExitVelocity();
                DashEnded?.Invoke();
            }

            return displacement;
        }

        Vector2 StepGround(Vector2 move, float deltaTime)
        {
            if (move.sqrMagnitude > 1f)
            {
                move = move.normalized;
            }

            var desired = move * CurrentMaxSpeed;
            var rate = move.sqrMagnitude > 0f ? settings.Acceleration : settings.Deceleration;

            Velocity = Vector2.MoveTowards(Velocity, desired, rate * deltaTime);

            return Velocity * deltaTime;
        }

        void ApplyDashExitVelocity()
        {
            var direction = dashVelocity.sqrMagnitude > 0f ? dashVelocity.normalized : Facing;
            Velocity = direction * (CurrentMaxSpeed * settings.DashExitSpeedFactor);
            dashVelocity = Vector2.zero;
        }

        static Vector2 RotateTowards(Vector2 from, Vector2 to, float maxRadians)
        {
            var delta = Vector2.SignedAngle(from, to) * Mathf.Deg2Rad;
            var step = Mathf.Clamp(delta, -maxRadians, maxRadians);
            var cos = Mathf.Cos(step);
            var sin = Mathf.Sin(step);

            return new Vector2(
                from.x * cos - from.y * sin,
                from.x * sin + from.y * cos);
        }
    }
}
