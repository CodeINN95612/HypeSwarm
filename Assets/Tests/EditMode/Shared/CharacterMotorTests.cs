using System;
using HypeSwarm.Shared.Movement;
using NUnit.Framework;
using UnityEngine;

namespace HypeSwarm.Shared.Tests
{
    /// <summary>
    /// How movement feels is not testable and is not tested here — that is what the Phase 1 exit
    /// criterion is for. What is testable is that the rules underneath it do what they claim:
    /// facing is independent of travel, a dash covers the distance it was authored with, and none
    /// of it changes with the frame rate.
    /// </summary>
    /// <remarks>
    /// Every fixture builds its own settings rather than using the shipping defaults. Those numbers
    /// are going to change every time someone plays the game, and a test that asserts against them
    /// would either break constantly or, worse, be updated to match whatever the code now does.
    /// </remarks>
    [TestFixture]
    public sealed class CharacterMotorTests
    {
        const float Tolerance = 0.001f;

        static MovementSettings Settings()
        {
            return new MovementSettings
            {
                MaxSpeed = 8f,
                Acceleration = 80f,
                Deceleration = 100f,
                TurnSpeed = 0f,
                DashDistance = 6f,
                DashDuration = 0.2f,
                DashCharges = 2,
                DashCooldown = 3f,
                DashLockout = 0.2f,
                DashExitSpeedFactor = 1f
            };
        }

        /// <summary>
        /// Runs the motor for a duration and returns the total displacement. The dash press is
        /// delivered on the first step only, as a real edge-triggered input would be.
        /// </summary>
        static Vector2 Run(CharacterMotor motor, MovementInput input, float duration, float step = 0.02f)
        {
            var total = Vector2.zero;
            var elapsed = 0f;
            var held = new MovementInput(input.Move, input.Aim);

            for (var first = true; elapsed < duration - 1e-6f; first = false)
            {
                var slice = Mathf.Min(step, duration - elapsed);
                total += motor.Step(first ? input : held, slice);
                elapsed += slice;
            }

            return total;
        }

        static void AssertDirection(Vector2 actual, Vector2 expected, string because)
        {
            Assert.That(actual.sqrMagnitude, Is.GreaterThan(0f), because);
            Assert.That(
                Vector2.Angle(actual.normalized, expected.normalized),
                Is.LessThan(0.5f),
                $"{because} (expected ~{expected}, got {actual})");
        }

        // --- Aiming is not travel (§5.5.1) -------------------------------------------------

        /// <summary>
        /// The whole control scheme in one assertion. If facing ever becomes a function of velocity
        /// this game is League-style, and every ability designed around aiming while retreating
        /// stops working.
        /// </summary>
        [Test]
        public void Facing_FollowsAim_WhileTravellingInAnotherDirection()
        {
            var motor = new CharacterMotor(Settings());

            var displacement = Run(motor, new MovementInput(Vector2.right, Vector2.up), 0.5f);

            AssertDirection(motor.Facing, Vector2.up, "facing should follow the aim");
            AssertDirection(displacement, Vector2.right, "travel should follow the movement input");
        }

        [Test]
        public void Facing_IsHeldWhenAimHasNoOpinion()
        {
            var motor = new CharacterMotor(Settings());

            Run(motor, new MovementInput(Vector2.zero, Vector2.right), 0.1f);
            var before = motor.Facing;
            Run(motor, new MovementInput(Vector2.up, Vector2.zero), 0.5f);

            Assert.That(motor.Facing, Is.EqualTo(before));
        }

        [Test]
        public void Facing_TurnsAtTheAuthoredRate_WhenATurnSpeedIsSet()
        {
            var settings = Settings();
            settings.TurnSpeed = 90f;

            var motor = new CharacterMotor(settings);
            motor.Reset(Vector2.up);

            Run(motor, new MovementInput(Vector2.zero, Vector2.right), 0.5f, step: 0.001f);

            Assert.That(Vector2.Angle(Vector2.up, motor.Facing), Is.EqualTo(45f).Within(1f));
        }

        [Test]
        public void Facing_StillFollowsAim_DuringADash()
        {
            var motor = new CharacterMotor(Settings());

            Run(motor, new MovementInput(Vector2.right, Vector2.up, dashPressed: true), 0.1f);

            Assert.That(motor.IsDashing, Is.True, "the dash should still be running");
            AssertDirection(motor.Facing, Vector2.up, "a dash must not lock the aim to the dash direction");
        }

        // --- Ground movement ---------------------------------------------------------------

        [Test]
        public void Speed_ReachesTopSpeedAndNeverExceedsIt()
        {
            var settings = Settings();
            var motor = new CharacterMotor(settings);
            var peak = 0f;

            for (var i = 0; i < 200; i++)
            {
                motor.Step(MovementInput.Moving(Vector2.right), 0.01f);
                peak = Mathf.Max(peak, motor.Velocity.magnitude);
            }

            Assert.That(peak, Is.EqualTo(settings.MaxSpeed).Within(Tolerance));
        }

        /// <summary>
        /// Holding two keys must not be faster than holding one. The bug is decades old, trivial to
        /// reintroduce by normalising in the wrong place, and immediately obvious to players — who
        /// will then run everywhere diagonally.
        /// </summary>
        [Test]
        public void DiagonalInput_IsNoFasterThanCardinalInput()
        {
            var cardinal = Run(new CharacterMotor(Settings()), MovementInput.Moving(Vector2.right), 2f);
            var diagonal = Run(new CharacterMotor(Settings()), MovementInput.Moving(Vector2.one), 2f);

            Assert.That(diagonal.magnitude, Is.EqualTo(cardinal.magnitude).Within(Tolerance));
        }

        [Test]
        public void PartialInput_MovesProportionallySlower()
        {
            var full = Run(new CharacterMotor(Settings()), MovementInput.Moving(Vector2.right), 2f);
            var half = Run(new CharacterMotor(Settings()), MovementInput.Moving(Vector2.right * 0.5f), 2f);

            Assert.That(half.magnitude, Is.LessThan(full.magnitude * 0.6f));
            Assert.That(half.magnitude, Is.GreaterThan(full.magnitude * 0.4f));
        }

        [Test]
        public void ReleasingInput_ComesToACompleteStop()
        {
            var motor = new CharacterMotor(Settings());

            Run(motor, MovementInput.Moving(Vector2.right), 1f);
            Run(motor, MovementInput.None, 1f);

            Assert.That(motor.Velocity, Is.EqualTo(Vector2.zero), "deceleration must not leave residue");
        }

        // --- Dash --------------------------------------------------------------------------

        /// <summary>
        /// Exit speed is zeroed here so the measurement is the dash and nothing else. With momentum
        /// carried out of it, this would be measuring the deceleration curve too.
        /// </summary>
        static MovementSettings IsolatedDashSettings()
        {
            var settings = Settings();
            settings.DashExitSpeedFactor = 0f;
            return settings;
        }

        [Test]
        public void Dash_CoversTheAuthoredDistance()
        {
            var settings = IsolatedDashSettings();
            var motor = new CharacterMotor(settings);

            var displacement = Run(motor, new MovementInput(Vector2.zero, Vector2.up, true), 1f);

            Assert.That(displacement.magnitude, Is.EqualTo(settings.DashDistance).Within(Tolerance));
        }

        /// <summary>
        /// A dash whose length depends on where frame boundaries fell is a dash that is a different
        /// ability at 30fps and at 144fps — and the netcode will re-simulate these steps at a rate
        /// they were never produced at (§10).
        /// </summary>
        [Test]
        public void Dash_CoversTheSameDistance_AtAnyStepSize()
        {
            var settings = IsolatedDashSettings();

            var slow = Run(new CharacterMotor(settings), new MovementInput(Vector2.zero, Vector2.up, true), 1f, 1f / 30f);
            var fast = Run(new CharacterMotor(settings), new MovementInput(Vector2.zero, Vector2.up, true), 1f, 1f / 240f);
            var single = Run(new CharacterMotor(settings), new MovementInput(Vector2.zero, Vector2.up, true), 1f, 1f);

            Assert.That(fast.magnitude, Is.EqualTo(slow.magnitude).Within(Tolerance));
            Assert.That(
                single.magnitude,
                Is.EqualTo(slow.magnitude).Within(Tolerance),
                "a step longer than the dash must still travel exactly one dash");
        }

        [Test]
        public void Dash_TravelsAlongMovementInput_NotAim()
        {
            var motor = new CharacterMotor(IsolatedDashSettings());

            var displacement = Run(motor, new MovementInput(Vector2.right, Vector2.up, true), 0.2f);

            AssertDirection(displacement, Vector2.right, "a dash follows where you are going, not where you are looking");
        }

        [Test]
        public void Dash_UsesFacing_WhenStandingStill()
        {
            var motor = new CharacterMotor(IsolatedDashSettings());

            var displacement = Run(motor, new MovementInput(Vector2.zero, Vector2.left, true), 0.2f);

            AssertDirection(displacement, Vector2.left, "a standing dash should go where the champion is looking");
        }

        [Test]
        public void Dash_IgnoresMovementInputUntilItEnds()
        {
            var settings = IsolatedDashSettings();
            var motor = new CharacterMotor(settings);
            var travelled = Vector2.zero;

            travelled += motor.Step(new MovementInput(Vector2.right, Vector2.up, true), 0.01f);

            while (motor.IsDashing)
            {
                travelled += motor.Step(new MovementInput(Vector2.left, Vector2.up), 0.01f);
            }

            AssertDirection(travelled, Vector2.right, "reversing mid-dash must not steer it");
            Assert.That(travelled.magnitude, Is.EqualTo(settings.DashDistance).Within(Tolerance));
        }

        [Test]
        public void Dash_IsRefusedWithoutACharge()
        {
            var settings = IsolatedDashSettings();
            settings.DashCharges = 1;

            var motor = new CharacterMotor(settings);
            var starts = 0;
            motor.DashStarted += _ => starts++;

            Run(motor, new MovementInput(Vector2.zero, Vector2.up, true), 1f);
            Run(motor, new MovementInput(Vector2.zero, Vector2.up, true), 1f);

            Assert.That(starts, Is.EqualTo(1), "the second dash has no charge to spend");
        }

        [Test]
        public void Dash_RaisesStartedAndEnded_ExactlyOnce()
        {
            var motor = new CharacterMotor(Settings());
            var started = 0;
            var ended = 0;

            motor.DashStarted += _ => started++;
            motor.DashEnded += () => ended++;

            Run(motor, new MovementInput(Vector2.right, Vector2.up, true), 1f);

            Assert.That(started, Is.EqualTo(1));
            Assert.That(ended, Is.EqualTo(1));
        }

        [Test]
        public void DashEvent_ReportsTheRemainingCharges()
        {
            var settings = Settings();
            var motor = new CharacterMotor(settings);
            var reported = -1;

            motor.DashStarted += dash => reported = dash.ChargesRemaining;

            motor.Step(new MovementInput(Vector2.zero, Vector2.up, true), 0.01f);

            Assert.That(reported, Is.EqualTo(settings.DashCharges - 1));
        }

        [Test]
        public void CancelDash_EndsTheDashAndRaisesEndedOnce()
        {
            var motor = new CharacterMotor(Settings());
            var ended = 0;
            motor.DashEnded += () => ended++;

            motor.Step(new MovementInput(Vector2.right, Vector2.up, true), 0.01f);
            motor.CancelDash();

            Assert.That(motor.IsDashing, Is.False);
            Assert.That(ended, Is.EqualTo(1));
        }

        [Test]
        public void CancelDash_WhileNotDashing_DoesNothing()
        {
            var motor = new CharacterMotor(Settings());
            var ended = 0;
            motor.DashEnded += () => ended++;

            motor.CancelDash();

            Assert.That(ended, Is.Zero);
        }

        // --- Degenerate inputs -------------------------------------------------------------

        [TestCase(0f)]
        [TestCase(-0.5f)]
        [TestCase(float.NaN)]
        public void NonPositiveDeltaTime_ProducesNoMotion(float deltaTime)
        {
            var motor = new CharacterMotor(Settings());

            Run(motor, MovementInput.Moving(Vector2.right), 0.5f);
            var velocity = motor.Velocity;

            var displacement = motor.Step(MovementInput.Moving(Vector2.right), deltaTime);

            Assert.That(displacement, Is.EqualTo(Vector2.zero));
            Assert.That(motor.Velocity, Is.EqualTo(velocity), "a dead frame must not advance the simulation");
        }

        /// <summary>
        /// Dash speed is distance over duration, so a duration dragged to zero in the inspector is
        /// not a very fast dash — it is a division by zero and a character somewhere near infinity.
        /// </summary>
        [Test]
        public void ZeroDashDuration_IsClampedRatherThanTeleporting()
        {
            var settings = IsolatedDashSettings();
            settings.DashDuration = 0f;

            var motor = new CharacterMotor(settings);
            var displacement = Run(motor, new MovementInput(Vector2.zero, Vector2.up, true), 1f);

            Assert.That(float.IsNaN(displacement.x) || float.IsInfinity(displacement.x), Is.False);
            Assert.That(displacement.magnitude, Is.EqualTo(settings.DashDistance).Within(Tolerance));
        }

        [Test]
        public void NegativeSettings_AreClampedInsteadOfInvertingMovement()
        {
            var settings = Settings();
            settings.MaxSpeed = -5f;
            settings.Acceleration = -5f;

            var motor = new CharacterMotor(settings);
            var displacement = Run(motor, MovementInput.Moving(Vector2.right), 1f);

            Assert.That(displacement, Is.EqualTo(Vector2.zero));
        }

        // --- Frame rate and determinism ----------------------------------------------------

        /// <summary>
        /// Exact equality is not achievable — acceleration is integrated per step — so the claim is
        /// the useful one: across the range of frame rates anyone will actually play at, the
        /// difference stays well below one frame of travel.
        /// </summary>
        [Test]
        public void GroundMovement_IsStableAcrossPlayableFrameRates()
        {
            var at60 = Run(new CharacterMotor(Settings()), MovementInput.Moving(Vector2.right), 2f, 1f / 60f);
            var at144 = Run(new CharacterMotor(Settings()), MovementInput.Moving(Vector2.right), 2f, 1f / 144f);

            Assert.That(at144.magnitude, Is.EqualTo(at60.magnitude).Within(0.1f));
        }

        /// <summary>
        /// Required by the netcode rather than by tidiness: clients simulate from shared inputs and
        /// a shared seed, and any hidden state that makes two runs differ shows up as a desync.
        /// </summary>
        [Test]
        public void SteppingTheSameInputs_IsDeterministic()
        {
            var random = new System.Random(20260909);
            var inputs = new MovementInput[600];

            for (var i = 0; i < inputs.Length; i++)
            {
                inputs[i] = new MovementInput(
                    new Vector2((float)random.NextDouble() * 2f - 1f, (float)random.NextDouble() * 2f - 1f),
                    new Vector2((float)random.NextDouble() * 2f - 1f, (float)random.NextDouble() * 2f - 1f),
                    random.Next(40) == 0);
            }

            var first = Replay(inputs);
            var second = Replay(inputs);

            Assert.That(second.Item1, Is.EqualTo(first.Item1));
            Assert.That(second.Item2, Is.EqualTo(first.Item2));
            Assert.That(second.Item3, Is.EqualTo(first.Item3));
        }

        static Tuple<Vector2, Vector2, int> Replay(MovementInput[] inputs)
        {
            var motor = new CharacterMotor(Settings());
            var position = Vector2.zero;
            var dashes = 0;

            motor.DashStarted += _ => dashes++;

            foreach (var input in inputs)
            {
                position += motor.Step(input, 1f / 60f);
            }

            return Tuple.Create(position, motor.Facing, dashes);
        }
    }
}
