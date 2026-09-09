using HypeSwarm.Shared.Stats;

namespace HypeSwarm.Shared.Combat
{
    /// <summary>
    /// The single funnel every point of damage in the game passes through.
    /// </summary>
    /// <remarks>
    /// <b>One choke point, on purpose</b> (Phase 2 step 5). Every augment ever written hooks here, so
    /// there must be exactly one place to hook — the moment a second path exists that damages
    /// something directly, half the augments in the game silently stop applying to half the damage,
    /// and nothing about that failure is visible until a player reports a number being wrong.
    ///
    /// <para><b>Order is fixed:</b> dodge, then damage reduction, then shields, then health. Dodge
    /// first because avoiding a hit should not consume a shield. Reduction before shields because a
    /// shield absorbs what actually arrives — reducing after absorption would make damage reduction
    /// worth less exactly when a shield is up, which is backwards.</para>
    ///
    /// <para><b>The attacker damage stat is not read here.</b> The packet arrives with scaling already
    /// applied, because each ability authors its own coefficients (§5.6.5). Penetration is the one
    /// attacker stat this reads, because it acts on the defender reduction rather than on the damage.</para>
    ///
    /// <para>The hook pipeline (§8.4) arrives in Phase 3 step 9 and belongs in
    /// <see cref="Apply"/> — PreDamage before mitigation, DamageCalculated after it, OnHit and OnKill
    /// after the result. That is why <see cref="Apply"/> exists at all rather than callers reaching
    /// for <see cref="IDamageable.TakeDamage"/> directly.</para>
    /// </remarks>
    public static class DamagePipeline
    {
        /// <summary>
        /// Applies a packet to a target. <b>The entry point — use this rather than
        /// <see cref="IDamageable.TakeDamage"/>.</b>
        /// </summary>
        public static DamageResult Apply(IDamageable target, in DamagePacket packet)
        {
            if (target == null || target.IsDead)
            {
                return DamageResult.None;
            }

            return target.TakeDamage(packet);
        }

        /// <summary>
        /// Runs the stat layer: dodge and damage reduction. Pure, so the formula is testable without
        /// an entity, a scene, or a network.
        /// </summary>
        /// <param name="attacker">The attacker sheet, or null for damage with no owner.</param>
        /// <param name="defender">The defender sheet, or null for something with no stats at all.</param>
        /// <param name="dodgeRoll">
        /// A roll in <c>[0, 1)</c> from the defender <see cref="DamageRandom"/>. Passed in rather than
        /// drawn here so this stays a function of its arguments.
        /// </param>
        public static DamageMitigation Mitigate(
            in DamagePacket packet,
            StatSheet attacker,
            StatSheet defender,
            float dodgeRoll)
        {
            var requested = packet.Amount;

            if (requested <= 0f || float.IsNaN(requested))
            {
                return DamageMitigation.Avoided(0f);
            }

            if (defender == null)
            {
                return DamageMitigation.Unmitigated(requested);
            }

            if (!packet.Has(DamageFlags.Undodgeable))
            {
                var dodge = defender.Effect(StatId.Dodge);

                if (dodge > 0f && dodgeRoll < dodge)
                {
                    return DamageMitigation.Avoided(requested);
                }
            }

            if (packet.Has(DamageFlags.IgnoresReduction))
            {
                return DamageMitigation.Unmitigated(requested);
            }

            var penetration = attacker == null ? 0f : attacker.Get(StatId.Penetration);
            var effective = EffectiveReduction(defender.Get(StatId.DamageReduction), penetration);
            var fraction = StatCurve.Diminishing(effective, defender.Catalog[StatId.DamageReduction].SoftCap);

            var amount = requested * (1f - fraction);

            if (amount < 0f)
            {
                amount = 0f;
            }

            return new DamageMitigation(requested, amount, false, fraction);
        }

        /// <summary>
        /// The defender damage reduction after the attacker penetration, before it is derived.
        /// </summary>
        /// <remarks>
        /// <b>Penetration removes reduction; it does not invert it.</b> It cannot push a defender
        /// below zero, so stacking it past what the target has is wasted rather than turning into
        /// amplification — otherwise a single penetration item would be the best damage item in the
        /// game against every unarmoured trash mob in a horde.
        ///
        /// <para>A defender whose reduction is <i>already</i> negative, from a shred debuff, keeps that
        /// amplification. Debuffs are allowed to do what penetration is not, because a debuff had to be
        /// applied to that specific target first.</para>
        /// </remarks>
        public static float EffectiveReduction(float reduction, float penetration)
        {
            if (penetration <= 0f || reduction <= 0f)
            {
                return reduction;
            }

            var left = reduction - penetration;

            return left < 0f ? 0f : left;
        }
    }
}
