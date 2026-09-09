using System;

namespace HypeSwarm.Shared.Combat
{
    /// <summary>
    /// One entity worth of health: the current value, the maximum it is measured against, the shields
    /// in front of it, and whether it is dead.
    /// </summary>
    /// <remarks>
    /// Pure C#, like <see cref="Stats.StatSheet"/> — no Unity types, no clock, no network. A champion, a
    /// trash mob and a boss all use this class, and the networked component around it is what differs.
    ///
    /// <para><b>Mitigation is not here.</b> The pool takes a <see cref="DamageMitigation"/> that the
    /// stat layer already produced and does the subtraction. Keeping the two apart means the formula
    /// that reads stat sheets is a static function, and the pool that owns mutable state has no stat
    /// sheet at all.</para>
    ///
    /// <para>Health is individual and never shared (§5). There is deliberately no team pool here.</para>
    /// </remarks>
    public sealed class HealthPool
    {
        /// <summary>The floor on maximum health, so nothing divides by zero computing a fraction.</summary>
        public const float MinimumMax = 1f;

        readonly ShieldPool shields = new ShieldPool();

        float max;
        float current;
        float sinceDamaged = float.MaxValue;

        /// <param name="max">Starting maximum. The pool starts full.</param>
        public HealthPool(float max)
        {
            this.max = max < MinimumMax ? MinimumMax : max;
            current = this.max;

            shields.Changed += () => Changed?.Invoke();
        }

        /// <summary>Any of the numbers moved. What a health bar subscribes to.</summary>
        public event Action Changed;

        /// <summary>Damage landed. Carries what happened, including the packet that killed.</summary>
        public event Action<DamageResult> Damaged;

        /// <summary>Health reached zero. Raised once; a dead pool refuses further damage.</summary>
        public event Action<DamageResult> Died;

        /// <summary>The pool came back. Death state is Phase 3 step 14; this is the state change it needs.</summary>
        public event Action Revived;

        public float Current => current;

        public float Max => max;

        /// <summary>Absorption sitting in front of health.</summary>
        public float Shield => shields.Total;

        /// <summary>Health plus shields — what a hit actually has to chew through.</summary>
        public float Total => current + shields.Total;

        /// <summary>Health as a share of maximum, ignoring shields. For a bar.</summary>
        public float Fraction => current / max;

        public bool IsDead => current <= 0f;

        public bool IsFull => current >= max;

        public ShieldPool Shields => shields;

        /// <summary>How long since damage landed. Regeneration waits on this.</summary>
        public float SecondsSinceDamaged => sinceDamaged;

        /// <summary>
        /// Changes the maximum, following the stat sheet.
        /// </summary>
        /// <remarks>
        /// <b>An increase is granted as current health, a decrease only clamps.</b> Granting it is
        /// what makes a max-health item useful the moment it is equipped rather than something that
        /// has to be healed into; not taking it back is what stops an expiring buff from killing the
        /// player it was helping, which is the single worst way for a temporary stat to behave.
        ///
        /// <para>A dead pool is not raised by a maximum going up.</para>
        /// </remarks>
        public void SetMax(float value)
        {
            var next = value < MinimumMax ? MinimumMax : value;

            if (next.Equals(max))
            {
                return;
            }

            var gained = next - max;
            var dead = IsDead;

            max = next;

            if (gained > 0f && !dead)
            {
                current += gained;
            }

            if (current > max)
            {
                current = max;
            }

            Changed?.Invoke();
        }

        /// <summary>
        /// Subtracts mitigated damage, shields first.
        /// </summary>
        /// <returns>What happened, for lifesteal, on-hit triggers, and damage numbers.</returns>
        public DamageResult Apply(in DamageMitigation mitigation)
        {
            if (IsDead)
            {
                return DamageResult.None;
            }

            if (mitigation.Dodged || mitigation.Amount <= 0f)
            {
                var avoided = new DamageResult(mitigation.Requested, mitigation.Amount, 0f, 0f, 0f, mitigation.Dodged, false);

                // No Damaged event: nothing landed, and a dodge that woke every on-hit trigger in the
                // game would make dodge worse than not having it.
                return avoided;
            }

            var absorbed = shields.Absorb(mitigation.Amount);
            var throughShields = mitigation.Amount - absorbed;

            var dealt = throughShields > current ? current : throughShields;
            var overkill = throughShields - dealt;

            current -= dealt;
            sinceDamaged = 0f;

            var killed = current <= 0f;

            if (killed)
            {
                current = 0f;
            }

            var result = new DamageResult(
                mitigation.Requested,
                mitigation.Amount,
                absorbed,
                dealt,
                overkill,
                false,
                killed);

            Changed?.Invoke();
            Damaged?.Invoke(result);

            if (killed)
            {
                shields.Clear();
                Died?.Invoke(result);
            }

            return result;
        }

        /// <summary>
        /// Restores health, up to the maximum.
        /// </summary>
        /// <remarks>Healing does not raise the dead — that is <see cref="Revive"/>, and it has rules.</remarks>
        /// <returns>How much landed. Zero on a full or dead pool, which is what an overheal readout wants.</returns>
        public float Heal(float amount)
        {
            if (IsDead || amount <= 0f || float.IsNaN(amount))
            {
                return 0f;
            }

            var headroom = max - current;

            if (headroom <= 0f)
            {
                return 0f;
            }

            var healed = amount > headroom ? headroom : amount;

            current += healed;

            Changed?.Invoke();

            return healed;
        }

        /// <summary>
        /// Advances shields and regeneration.
        /// </summary>
        /// <param name="regenPerSecond">The health regen stat, already resolved.</param>
        /// <param name="regenDelay">Seconds of not being hit before regeneration resumes.</param>
        public void Tick(float deltaTime, float regenPerSecond, float regenDelay)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            // Shields tick on the dead too, so a corpse does not come back holding the shield it died
            // with. Clear on death already handles that, but a pool revived and re-shielded should not
            // depend on that ordering.
            shields.Tick(deltaTime);

            if (IsDead)
            {
                return;
            }

            if (sinceDamaged < float.MaxValue - deltaTime)
            {
                sinceDamaged += deltaTime;
            }

            if (regenPerSecond <= 0f || sinceDamaged < regenDelay)
            {
                return;
            }

            Heal(regenPerSecond * deltaTime);
        }

        /// <summary>
        /// Brings a dead pool back at a share of maximum.
        /// </summary>
        /// <remarks>
        /// The state change only. Which revive route was used, what it cost, and the respawn timer are
        /// Phase 3 step 14 (§5) — none of that belongs to a health pool.
        /// </remarks>
        public void Revive(float healthFraction)
        {
            if (!IsDead)
            {
                return;
            }

            var fraction = healthFraction > 1f ? 1f : healthFraction;

            if (fraction <= 0f || float.IsNaN(fraction))
            {
                fraction = 1f;
            }

            current = max * fraction;

            if (current < MinimumMax)
            {
                current = MinimumMax;
            }

            sinceDamaged = 0f;

            Changed?.Invoke();
            Revived?.Invoke();
        }

        /// <summary>Kills outright, bypassing mitigation and shields. For despawns and the out-of-bounds case.</summary>
        public DamageResult Kill()
        {
            if (IsDead)
            {
                return DamageResult.None;
            }

            var lethal = current;

            current = 0f;
            sinceDamaged = 0f;

            var result = new DamageResult(lethal, lethal, 0f, lethal, 0f, false, true);

            Changed?.Invoke();
            Damaged?.Invoke(result);

            shields.Clear();
            Died?.Invoke(result);

            return result;
        }
    }
}
