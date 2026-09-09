namespace HypeSwarm.Shared.Combat
{
    /// <summary>
    /// Something damage can be applied to.
    /// </summary>
    /// <remarks>
    /// Deliberately narrow. A champion, a trash mob, a boss, and a destructible siege structure are
    /// wildly different objects, and the only thing an attacker needs to know about any of them is
    /// that a packet can be handed over and a result comes back.
    ///
    /// <para>Server-side. Damage is host-authoritative (§10), so an implementation may assume it is
    /// only ever called on the host, and a client that wants to show a predicted hit shows it without
    /// going through here.</para>
    /// </remarks>
    public interface IDamageable
    {
        /// <summary>Refuses further damage and does not die twice.</summary>
        bool IsDead { get; }

        /// <summary>Applies a packet. Prefer <see cref="DamagePipeline.Apply"/>, which is the choke point.</summary>
        DamageResult TakeDamage(in DamagePacket packet);
    }
}
