using System;
using System.Collections.Generic;

namespace HypeSwarm.Shared.Abilities
{
    /// <summary>
    /// One entity's abilities, in an indexed array, plus the cast currently in progress (§8.3).
    /// </summary>
    /// <remarks>
    /// <b>Indexed, not named.</b> There are no <c>Q</c>, <c>W</c>, <c>E</c> fields, which is what makes
    /// "your third ability is replaced with X" an assignment rather than a refactor, and what lets
    /// input, the HUD and augments all handle slots uniformly. The five-slot layout is a convention
    /// expressed through <see cref="AbilityRole"/> on top of this, not a structure underneath it.
    ///
    /// <para><b>One cast at a time.</b> That restriction is what makes cast cost a real cost (§5.5.4):
    /// if a rooted burst could be started while a channel was running, paying the root would cost
    /// nothing. A second cast is refused rather than queued — a queue would mean an input landing a
    /// frame early fires when the player no longer wants it, which in a horde game kills people.</para>
    ///
    /// <para>Plain C#: no Unity types, no clock, no network. Both the host and the casting client run
    /// one of these, independently, over the same definitions; the host's is authoritative and the
    /// client's is what makes the cooldown on screen respond the instant a key goes down.</para>
    /// </remarks>
    public sealed class AbilityBook
    {
        /// <summary>
        /// Most passive pulses one <see cref="Tick"/> may produce.
        /// </summary>
        /// <remarks>
        /// A frame that took a second — a hitch, a breakpoint, a scene load — must not be paid back as
        /// a second of passive damage arriving at once. Beyond this the owed time is dropped, which is
        /// the lesser wrong: a passive that quietly skips a pulse during a stall is invisible, and one
        /// that deals a burst of damage during a stall looks like a bug to whoever it kills.
        /// </remarks>
        public const int MaxPulsesPerTick = 2;

        readonly AbilityInstance[] slots;

        float castElapsed;
        float channelElapsed;
        float passiveAccumulator;
        int activeSlot = -1;

        public AbilityBook(IReadOnlyList<IAbilityDefinition> definitions)
        {
            var count = definitions?.Count ?? 0;

            slots = new AbilityInstance[count];

            for (var i = 0; i < count; i++)
            {
                var definition = definitions[i];

                if (definition != null)
                {
                    slots[i] = new AbilityInstance(definition, i);
                }
            }
        }

        /// <summary>How many slots there are. Five for a champion, fewer for an elite (§6.4).</summary>
        public int Count => slots.Length;

        /// <summary>The instance in a slot, or null for an empty or out-of-range one.</summary>
        public AbilityInstance this[int index] =>
            index >= 0 && index < slots.Length ? slots[index] : null;

        public bool IsCasting => activeSlot >= 0;

        /// <summary>What is being cast, or null.</summary>
        public AbilityInstance Casting => this[activeSlot];

        /// <summary>Whether a channel is being held right now.</summary>
        public bool IsChannelling =>
            IsCasting && Casting.Definition.CastCost == CastCost.Channelled;

        /// <summary>Seconds the current channel has been held. What a channelled effect scales from.</summary>
        public float ChannelElapsed => channelElapsed;

        /// <summary>How far through its cast time the current cast is, from 0 to 1. For the cast bar.</summary>
        public float CastProgress
        {
            get
            {
                if (!IsCasting)
                {
                    return 0f;
                }

                var castTime = Casting.Definition.CastTime;

                return castTime <= 0f ? 1f : Clamp01(castElapsed / castTime);
            }
        }

        /// <summary>How far through the longest allowed channel this is, from 0 to 1.</summary>
        public float ChannelProgress
        {
            get
            {
                if (!IsChannelling)
                {
                    return 0f;
                }

                var longest = Casting.Definition.MaxChannelDuration;

                return longest <= 0f ? 0f : Clamp01(channelElapsed / longest);
            }
        }

        /// <summary>A cast began. Presentation starts an animation here; nothing has happened yet.</summary>
        public event Action<AbilityInstance> CastStarted;

        /// <summary>
        /// A cast resolved and its effect steps should run now. The float is how long a channel was
        /// held, and zero for everything else.
        /// </summary>
        public event Action<AbilityInstance, float> CastResolved;

        /// <summary>A cast was cut short and will not resolve. The charge stays spent.</summary>
        public event Action<AbilityInstance> CastInterrupted;

        /// <summary>
        /// The passive should run. The float is how many seconds this pulse stands for, so a passive
        /// authored as a rate keeps its rate when the tick interval is retuned for performance (§5.5.3).
        /// </summary>
        public event Action<AbilityInstance, float> PassivePulse;

        /// <summary>The first slot holding this role, or -1.</summary>
        public int IndexOf(AbilityRole role)
        {
            for (var i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null && slots[i].Role == role)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>The first ability with this role, or null. How input and augments address slots.</summary>
        public AbilityInstance Find(AbilityRole role) => this[IndexOf(role)];

        /// <summary>
        /// Begins a cast, or explains why not.
        /// </summary>
        /// <remarks>
        /// The charge is spent here, at the start, not when the effect lands. A cast interrupted halfway
        /// has still cost its cooldown, which is what makes committing to a channel a decision rather
        /// than a free option (§5.5.4).
        /// </remarks>
        /// <param name="hasteEffect">Derived haste, in <c>(-1, 1)</c>. Read once, at this moment.</param>
        public CastOutcome TryBegin(int slot, float hasteEffect = 0f)
        {
            var instance = this[slot];

            if (instance == null)
            {
                return CastOutcome.UnknownSlot;
            }

            if (!AbilityRoles.IsCast(instance.Role))
            {
                return CastOutcome.NotCastable;
            }

            if (IsCasting)
            {
                return CastOutcome.Busy;
            }

            if (!instance.TryActivate(hasteEffect))
            {
                return CastOutcome.OnCooldown;
            }

            activeSlot = slot;
            castElapsed = 0f;
            channelElapsed = 0f;

            CastStarted?.Invoke(instance);

            // An ability with no wind-up resolves on the frame the key went down. Waiting a frame
            // would be a frame of latency on the thing players are most sensitive to.
            if (instance.Definition.CastCost != CastCost.Channelled && instance.Definition.CastTime <= 0f)
            {
                Resolve();
            }

            return CastOutcome.Started;
        }

        /// <summary>Lets go of a channel, resolving it now. False when nothing is being channelled.</summary>
        public bool ReleaseChannel()
        {
            if (!IsChannelling)
            {
                return false;
            }

            Resolve();

            return true;
        }

        /// <summary>Cuts the current cast short without resolving it. Death, a stun, a hard interrupt.</summary>
        public bool Interrupt()
        {
            if (!IsCasting)
            {
                return false;
            }

            var instance = Casting;

            activeSlot = -1;
            castElapsed = 0f;
            channelElapsed = 0f;

            CastInterrupted?.Invoke(instance);

            return true;
        }

        /// <summary>
        /// Advances cooldowns, the cast in progress, and the passive pulse.
        /// </summary>
        /// <param name="passiveInterval">
        /// Seconds between passive pulses (<see cref="AbilitySettings.PassiveTickInterval"/>). Zero
        /// stops the passive entirely, which is what a test that only cares about cooldowns wants.
        /// </param>
        public void Tick(float deltaTime, float passiveInterval = 0f)
        {
            if (float.IsNaN(deltaTime) || deltaTime <= 0f)
            {
                return;
            }

            for (var i = 0; i < slots.Length; i++)
            {
                slots[i]?.Tick(deltaTime);
            }

            TickCast(deltaTime);
            TickPassive(deltaTime, passiveInterval);
        }

        /// <summary>
        /// What top speed is multiplied by right now, from the cast cost of whatever is being cast
        /// (§5.5.4). One when nothing is.
        /// </summary>
        public float MovementMultiplier(float slowedCastSpeed)
        {
            return IsCasting
                ? CastCosts.SpeedMultiplier(Casting.Definition.CastCost, slowedCastSpeed)
                : 1f;
        }

        /// <summary>Returns every charge and drops any cast in progress. Respawns and test setup.</summary>
        public void Reset()
        {
            Interrupt();
            passiveAccumulator = 0f;

            for (var i = 0; i < slots.Length; i++)
            {
                slots[i]?.Refresh();
            }
        }

        void TickCast(float deltaTime)
        {
            if (!IsCasting)
            {
                return;
            }

            var definition = Casting.Definition;

            if (definition.CastCost == CastCost.Channelled)
            {
                channelElapsed += deltaTime;

                // The player chooses when to let go, and the ceiling chooses for them if they do not.
                if (definition.MaxChannelDuration > 0f && channelElapsed >= definition.MaxChannelDuration)
                {
                    channelElapsed = definition.MaxChannelDuration;
                    Resolve();
                }

                return;
            }

            castElapsed += deltaTime;

            if (castElapsed >= definition.CastTime)
            {
                Resolve();
            }
        }

        void TickPassive(float deltaTime, float passiveInterval)
        {
            if (passiveInterval <= 0f)
            {
                return;
            }

            var passive = Find(AbilityRole.Passive);

            if (passive == null)
            {
                return;
            }

            passiveAccumulator += deltaTime;

            var pulses = 0;

            while (passiveAccumulator >= passiveInterval && pulses < MaxPulsesPerTick)
            {
                passiveAccumulator -= passiveInterval;
                pulses++;

                PassivePulse?.Invoke(passive, passiveInterval);
            }

            if (passiveAccumulator >= passiveInterval)
            {
                // Owed more than the catch-up allows. Drop it rather than pay it back as a burst.
                passiveAccumulator = 0f;
            }
        }

        void Resolve()
        {
            var instance = Casting;
            var held = channelElapsed;

            activeSlot = -1;
            castElapsed = 0f;
            channelElapsed = 0f;

            CastResolved?.Invoke(instance, held);
        }

        static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
    }
}
