using System;
using System.Collections.Generic;
using HypeSwarm.Shared.Combat;
using HypeSwarm.Shared.Net;
using HypeSwarm.Shared.Stats;
using HypeSwarm.Shared.Tuning;
using Mirror;
using UnityEngine;

namespace HypeSwarm.Shared.Abilities
{
    /// <summary>
    /// The networked half of the ability system: one <see cref="AbilityBook"/> per entity, and the
    /// agreement between the casting client and the host about what a cast did.
    /// </summary>
    /// <remarks>
    /// <b>Both machines run the same book over the same definitions, and they run it for different
    /// reasons.</b> The client runs it so the dash happens on the frame the key went down and the
    /// cooldown on screen starts immediately; the host runs it so the damage is decided by the machine
    /// nobody can edit. Neither is a copy of the other: they are two independent state machines that
    /// agree because they were given the same authored data (§5.5.1, §10).
    ///
    /// <para>The split inside a cast is which steps run where. A step declares
    /// <see cref="IEffectStep.RunsOnPrediction"/> only if the player must feel it at once — the dash,
    /// the animation — and everything that changes the world runs with authority only. A client that
    /// applied its own damage would be a client that decides what dies.</para>
    ///
    /// <para><b>The host is allowed to say no.</b> When it does, the client hands back the charge it
    /// spent rather than refilling it, so a refused cast costs nothing and an accepted one costs exactly
    /// what it should.</para>
    ///
    /// <para>What this class deliberately does not do: decide anything. Cooldowns, charges, cast times,
    /// channels and the passive tick are all <see cref="AbilityBook"/>, which is pure C# and tested
    /// without a scene. This is the part that cannot be — components, sync, and a clock.</para>
    /// </remarks>
    [AddComponentMenu("Hype Swarm/Ability Runner")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Health))]
    public sealed class AbilityRunner : NetworkBehaviour
    {
        [SerializeField]
        [Tooltip("The champion whose five abilities this entity has. Elites use the same component " +
                 "with fewer abilities.")]
        ChampionDefinition champion;

        AbilityBook book;
        Health health;
        ChampionStats stats;
        IMobility mobility;
        AbilityAim activeAim;

        /// <summary>The abilities and their state on this machine. Read it; the runner drives it.</summary>
        public AbilityBook Book => book;

        /// <summary>Which champion this is, for the HUD.</summary>
        public ChampionDefinition Champion => champion;

        /// <summary>
        /// What top speed should be multiplied by right now, from the cast cost of whatever is being
        /// cast (§5.5.4). The movement side reads this; the ability side never touches the motor.
        /// </summary>
        public float MovementMultiplier =>
            book == null ? 1f : book.MovementMultiplier(GameTuning.Abilities.SlowedCastSpeed);

        /// <summary>A cast started on this machine — ours, or the host's copy of ours.</summary>
        public event Action<AbilityInstance> CastStarted;

        /// <summary>
        /// Somebody else's champion cast something. <b>For presentation only</b>: nothing about the
        /// outcome arrives this way, because the outcome is the host's and is replicated as its effects.
        /// </summary>
        public event Action<AbilityInstance> CastObserved;

        /// <summary>The host refused a cast this client had already started. The charge has been returned.</summary>
        public event Action<AbilityInstance> CastRefused;

        void Awake()
        {
            TryGetComponent(out health);
            TryGetComponent(out stats);

            book = new AbilityBook(champion != null ? champion.Abilities : null);
            book.CastStarted += OnCastStarted;
            book.CastResolved += OnCastResolved;
            book.PassivePulse += OnPassivePulse;

            if (champion == null)
            {
                Debug.LogWarning($"{name} has an ability runner with no champion assigned, so it has no abilities.", this);
            }
        }

        void OnDestroy()
        {
            if (book == null)
            {
                return;
            }

            book.CastStarted -= OnCastStarted;
            book.CastResolved -= OnCastResolved;
            book.PassivePulse -= OnPassivePulse;
        }

        public override void OnStartServer()
        {
            if (health != null)
            {
                health.Died += OnDied;
            }
        }

        public override void OnStopServer()
        {
            if (health != null)
            {
                health.Died -= OnDied;
            }
        }

        /// <summary>
        /// Takes the motor once this machine owns the champion.
        /// </summary>
        /// <remarks>
        /// Authority, not merely being a client. Every machine has a <c>ChampionController</c> component
        /// on every champion — four of them disabled — and handing a dash step one of those would
        /// teleport somebody else's champion locally.
        /// </remarks>
        public override void OnStartAuthority()
        {
            mobility = GetComponent<IMobility>();

            if (!isServer && health != null)
            {
                health.Died += OnDied;
            }
        }

        public override void OnStopAuthority()
        {
            mobility = null;

            if (!isServer && health != null)
            {
                health.Died -= OnDied;
            }
        }

        void Update()
        {
            // Only the two machines that need it: the host, which decides, and the owner, which
            // predicts. A bystander's copy would tick cooldowns nobody reads.
            if (book == null || (!isServer && !isOwned))
            {
                return;
            }

            book.Tick(Time.deltaTime, GameTuning.Abilities.PassiveTickInterval);
        }

        // --- Owner API ---------------------------------------------------------------------

        /// <summary>Casts whatever is in the slot with this role. How input addresses abilities.</summary>
        public CastOutcome RequestCast(AbilityRole role, AbilityAim aim)
        {
            return RequestCast(book == null ? -1 : book.IndexOf(role), aim);
        }

        /// <summary>
        /// Casts the ability in a slot, predicting it locally and asking the host to confirm.
        /// </summary>
        /// <remarks>
        /// Returns what <i>this</i> machine decided. The host may still refuse, which arrives later as
        /// <see cref="CastRefused"/> — there is no version of this that can answer for the host without
        /// waiting a round trip, and waiting a round trip is the thing being avoided.
        /// </remarks>
        public CastOutcome RequestCast(int slot, AbilityAim aim)
        {
            if (book == null)
            {
                return CastOutcome.UnknownSlot;
            }

            if (health != null && health.IsDead)
            {
                return CastOutcome.Dead;
            }

            var instance = book[slot];

            if (instance == null)
            {
                return CastOutcome.UnknownSlot;
            }

            activeAim = Clamp(aim, instance.Definition);

            var outcome = book.TryBegin(slot, Haste());

            if (outcome != CastOutcome.Started)
            {
                return outcome;
            }

            // On a host this already ran with authority, so asking the server would cast it twice.
            if (!isServer)
            {
                CmdCast(slot, activeAim.Point);
            }

            return outcome;
        }

        /// <summary>Lets go of a channel. Resolves it here and tells the host to do the same.</summary>
        public bool ReleaseChannel()
        {
            if (book == null || !book.IsChannelling)
            {
                return false;
            }

            var released = book.ReleaseChannel();

            if (released && !isServer)
            {
                CmdReleaseChannel();
            }

            return released;
        }

        /// <summary>Drops whatever is being cast. Death, a stun, a hard interrupt.</summary>
        public bool Interrupt() => book != null && book.Interrupt();

        // --- Host ---------------------------------------------------------------------------

        /// <summary>
        /// A client asking to cast.
        /// </summary>
        /// <remarks>
        /// Only the aim point crosses the wire, and it is clamped here against the ability's authored
        /// range rather than trusted — a point past it is either a bad frame or a client casting across
        /// the map, and clamping is the right answer to both. The slot, the cooldown, the charges and the
        /// cast time are all the host's own book; nothing a client says about them is read.
        /// </remarks>
        [Command]
        void CmdCast(int slot, Vector3 aimPoint)
        {
            if (book == null)
            {
                return;
            }

            var instance = book[slot];

            if (instance == null)
            {
                return;
            }

            if (health != null && health.IsDead)
            {
                TargetRefuseCast(slot);

                return;
            }

            activeAim = Clamp(AbilityAim.FromPoint(transform.position, aimPoint, FacingFallback()), instance.Definition);

            var outcome = book.TryBegin(slot, Haste());

            if (outcome != CastOutcome.Started)
            {
                TargetRefuseCast(slot);

                return;
            }

            RpcCastObserved(slot);
        }

        /// <summary>
        /// A client letting go of a channel.
        /// </summary>
        /// <remarks>
        /// <b>How long it was held is the host's own measurement</b>, not a number the client sent. The
        /// two differ by network jitter and agreeing with the client would make a channelled ultimate
        /// scale with a value the client chooses.
        /// </remarks>
        [Command]
        void CmdReleaseChannel()
        {
            book?.ReleaseChannel();
        }

        [TargetRpc]
        void TargetRefuseCast(int slot)
        {
            var instance = book?[slot];

            if (instance == null)
            {
                return;
            }

            // Interrupt first: a refused cast that was still winding up must not resolve locally.
            if (book.IsCasting && book.Casting == instance)
            {
                book.Interrupt();
            }

            instance.Refund();

            CastRefused?.Invoke(instance);
        }

        /// <summary>
        /// Tells everyone else that this champion cast something, for their presentation.
        /// </summary>
        /// <remarks>
        /// No effect steps run from this. What the cast did arrives as its consequences — health
        /// numbers, shields, modifiers — all of which are already replicated by the systems that own
        /// them. Running steps here would mean four machines each deciding what a fifth one hit.
        /// </remarks>
        [ClientRpc(includeOwner = false)]
        void RpcCastObserved(int slot)
        {
            var instance = book?[slot];

            if (instance != null)
            {
                CastObserved?.Invoke(instance);
            }
        }

        // --- Running a cast -----------------------------------------------------------------

        void OnCastStarted(AbilityInstance instance)
        {
            CastStarted?.Invoke(instance);

            // Before the cast time, not after it: these are the steps that have to happen during the
            // commitment rather than as its payoff — the guard a channel grants while it is held.
            Run(instance, instance.Definition.StartSteps, activeAim, 0f, 0f);
        }

        void OnCastResolved(AbilityInstance instance, float channelDuration)
        {
            Run(instance, instance.Definition.Steps, activeAim, channelDuration, 0f);
        }

        void OnPassivePulse(AbilityInstance instance, float interval)
        {
            // A passive is not aimed. It is centred on whoever has it, every time.
            Run(
                instance,
                instance.Definition.Steps,
                AbilityAim.FromDirection(transform.position, FacingFallback()),
                0f,
                interval);
        }

        void Run(
            AbilityInstance instance,
            IReadOnlyList<IEffectStep> steps,
            AbilityAim aim,
            float channelDuration,
            float deltaTime)
        {
            if (instance?.Definition == null || health == null)
            {
                return;
            }

            if (steps == null || steps.Count == 0)
            {
                return;
            }

            var authority = isServer;

            var context = new AbilityContext
            {
                Caster = health,
                Ability = instance.Definition,
                Slot = instance.Slot,
                Source = instance.Source,
                Aim = aim,
                IsAuthority = authority,
                World = CombatantWorld.Shared,
                Mobility = mobility,
                ChannelDuration = channelDuration,
                DeltaTime = deltaTime
            };

            for (var i = 0; i < steps.Count; i++)
            {
                var step = steps[i];

                if (step == null)
                {
                    continue;
                }

                if (authority ? !step.RunsOnAuthority : !step.RunsOnPrediction)
                {
                    continue;
                }

                try
                {
                    step.Execute(context);
                }
                catch (Exception exception)
                {
                    // One bad authored step must not take the rest of the cast with it, and must not
                    // leave combat in a half-resolved state on one machine only. Loudly, though:
                    // content that throws is content that is wrong.
                    Debug.LogError(
                        $"[Abilities] Step {i} of {instance.Definition.Id.Value} threw: {exception}", this);
                }
            }
        }

        void OnDied()
        {
            book?.Interrupt();
        }

        AbilityAim Clamp(AbilityAim aim, IAbilityDefinition definition)
        {
            if (definition == null)
            {
                return aim;
            }

            switch (definition.Targeting)
            {
                case TargetingMode.Self:
                    return AbilityAim.FromDirection(transform.position, FacingFallback());

                case TargetingMode.AimDirection:
                    // The point is ignored, but a direction with no point behind it still needs one for
                    // any step centred on where the ability was pointed.
                    return aim.HasDirection
                        ? AbilityAim.FromDirection(transform.position, aim.Direction)
                        : AbilityAim.FromDirection(transform.position, FacingFallback());

                default:
                    return aim.ClampedTo(transform.position, definition.Range);
            }
        }

        /// <summary>
        /// Which way this champion is pointing, for a cast with no usable aim direction.
        /// </summary>
        /// <remarks>
        /// Read off the replicated facing rather than the transform, because the root never turns — the
        /// visual does (§12) — so the transform forward would be the direction the champion spawned in.
        /// </remarks>
        Vector2 FacingFallback()
        {
            return TryGetComponent(out ChampionNetworkState state) ? state.Facing : Vector2.up;
        }

        float Haste() => stats?.Sheet == null ? 0f : stats.Sheet.Effect(StatId.Haste);
    }
}
