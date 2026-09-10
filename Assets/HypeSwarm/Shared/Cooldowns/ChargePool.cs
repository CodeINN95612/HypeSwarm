using System;
using UnityEngine;

namespace HypeSwarm.Shared.Cooldowns
{
    /// <summary>
    /// Uses of an ability, each recharging on its own timer, with a lockout between them.
    /// </summary>
    /// <remarks>
    /// Every ability has one of these, including the ordinary single-charge kind — a cooldown is a
    /// charge pool of one, and having one implementation means haste, charges and the lockout cannot
    /// disagree about what is ready.
    ///
    /// <para>The two properties here are both called out in §5.5.3 for the mobility slot in
    /// particular, and both are easy to get subtly wrong.</para>
    ///
    /// <para><b>Independent cooldowns.</b> Spending two charges starts two timers that run at the
    /// same time, so both return one cooldown after their own use. The alternative — a single timer
    /// that refills one charge at a time — makes the second charge take twice as long and turns
    /// mobility into a trickle. Which one is wanted is a design decision; this is the one the spec
    /// asked for, and the test suite pins it so a refactor cannot quietly swap it.</para>
    ///
    /// <para><b>The lockout.</b> Without it a single frame of input can spend two charges, and the
    /// player experiences that as the game eating their escape. It is not a balance number.</para>
    ///
    /// <para>Time is passed in rather than read. No wall clock, no <c>Time.deltaTime</c>: the pool
    /// is steppable at any rate, which is what lets it be tested in microseconds and re-simulated
    /// during netcode reconciliation later.</para>
    /// </remarks>
    public sealed class ChargePool
    {
        /// <summary>Seconds remaining before each slot returns. Zero means the charge is ready.</summary>
        float[] cooldowns = Array.Empty<float>();

        float cooldownDuration;
        float lockoutDuration;

        public ChargePool(int capacity = 1, float cooldown = 1f, float lockout = 0f)
        {
            Configure(capacity, cooldown, lockout, startFull: true);
        }

        public int Capacity => cooldowns.Length;

        public float LockoutRemaining { get; private set; }

        public int Available
        {
            get
            {
                var ready = 0;

                for (var i = 0; i < cooldowns.Length; i++)
                {
                    if (cooldowns[i] <= 0f)
                    {
                        ready++;
                    }
                }

                return ready;
            }
        }

        public bool CanSpend => LockoutRemaining <= 0f && Available > 0;

        /// <summary>
        /// Seconds until the next charge returns, or zero when one is already available. For the
        /// cooldown readout; the pool itself never consults it.
        /// </summary>
        public float NextChargeIn
        {
            get
            {
                var soonest = float.PositiveInfinity;

                for (var i = 0; i < cooldowns.Length; i++)
                {
                    if (cooldowns[i] > 0f && cooldowns[i] < soonest)
                    {
                        soonest = cooldowns[i];
                    }
                }

                return float.IsPositiveInfinity(soonest) ? 0f : soonest;
            }
        }

        /// <summary>
        /// Applies settings, resizing if the charge count changed. Called every step so that a
        /// number dragged in the inspector during play takes effect immediately.
        /// </summary>
        /// <remarks>
        /// Added charges arrive ready. Removed ones are dropped from the end, which is arbitrary,
        /// but shrinking the pool only ever happens while tuning.
        /// </remarks>
        public void Configure(int capacity, float cooldown, float lockout, bool startFull = false)
        {
            capacity = Mathf.Max(0, capacity);
            cooldownDuration = Mathf.Max(0f, cooldown);
            lockoutDuration = Mathf.Max(0f, lockout);

            if (cooldowns.Length == capacity)
            {
                if (startFull)
                {
                    Refill();
                }

                return;
            }

            var resized = new float[capacity];
            var carried = Mathf.Min(cooldowns.Length, capacity);

            if (!startFull)
            {
                Array.Copy(cooldowns, resized, carried);
            }

            cooldowns = resized;
        }

        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            for (var i = 0; i < cooldowns.Length; i++)
            {
                if (cooldowns[i] > 0f)
                {
                    cooldowns[i] = Mathf.Max(0f, cooldowns[i] - deltaTime);
                }
            }

            LockoutRemaining = Mathf.Max(0f, LockoutRemaining - deltaTime);
        }

        /// <summary>Spends one charge if the pool allows it. Returns false rather than throwing.</summary>
        public bool TrySpend()
        {
            if (!CanSpend)
            {
                return false;
            }

            for (var i = 0; i < cooldowns.Length; i++)
            {
                if (cooldowns[i] <= 0f)
                {
                    cooldowns[i] = cooldownDuration;
                    LockoutRemaining = lockoutDuration;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Hands one charge back — the most recently spent one.
        /// </summary>
        /// <remarks>
        /// For a client whose predicted cast the host refused. The client has already spent the charge
        /// locally so the cooldown reads correctly the instant the key is pressed; when the host says
        /// no, giving back everything would be a free refill and giving back nothing would charge the
        /// player for a cast that never happened.
        ///
        /// <para>Most recent rather than soonest, because every charge is on the same duration, so the
        /// one with the longest left is the one just spent.</para>
        /// </remarks>
        /// <returns>False when nothing was on cooldown to give back.</returns>
        public bool Refund()
        {
            var newest = -1;
            var longest = 0f;

            for (var i = 0; i < cooldowns.Length; i++)
            {
                if (cooldowns[i] > longest)
                {
                    newest = i;
                    longest = cooldowns[i];
                }
            }

            if (newest < 0)
            {
                return false;
            }

            cooldowns[newest] = 0f;
            LockoutRemaining = 0f;

            return true;
        }

        /// <summary>Returns every charge and clears the lockout. For respawns and test setup.</summary>
        public void Refill()
        {
            for (var i = 0; i < cooldowns.Length; i++)
            {
                cooldowns[i] = 0f;
            }

            LockoutRemaining = 0f;
        }

        /// <summary>Seconds remaining on one slot. Exposed for tests and the cooldown readout.</summary>
        public float CooldownRemaining(int slot)
        {
            return slot >= 0 && slot < cooldowns.Length ? cooldowns[slot] : 0f;
        }
    }
}
