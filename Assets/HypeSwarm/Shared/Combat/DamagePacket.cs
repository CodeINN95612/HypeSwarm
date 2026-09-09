using HypeSwarm.Shared.Stats;

namespace HypeSwarm.Shared.Combat
{
    /// <summary>
    /// One attempt to damage one target. The argument every point of damage in the game is carried
    /// in, and the thing augments will read and rewrite.
    /// </summary>
    /// <remarks>
    /// <b>The amount already includes the attacker scaling.</b> An ability multiplies its own
    /// coefficient against the caster damage stat and puts the product here (§5.6.5); the pipeline
    /// never reads the attacker damage stat itself. That is the one-way dependency in practice — if
    /// the pipeline applied scaling, the scaling would be a property of the pipeline rather than of
    /// the ability, and no two abilities could scale differently.
    ///
    /// <para>Penetration is the exception, and it belongs here rather than in the ability, because it
    /// acts on the <i>defender</i> reduction. It is read from the attacker sheet at mitigation time.</para>
    ///
    /// <para>The attacker is a net id rather than a component reference. Packets outlive the frame
    /// they were made in when a damage-over-time applies them later, and a reference would keep a
    /// dead champion alive; an id that no longer resolves simply means an unattributed hit, which is
    /// the correct outcome.</para>
    /// </remarks>
    public readonly struct DamagePacket
    {
        public DamagePacket(float amount, ModifierSource source, DamageFlags flags = DamageFlags.None, uint attackerNetId = 0u)
        {
            Amount = amount;
            Source = source;
            Flags = flags;
            AttackerNetId = attackerNetId;
        }

        /// <summary>Damage before mitigation, with the attacker scaling already applied.</summary>
        public float Amount { get; }

        /// <summary>What dealt it. The same source vocabulary modifiers use, so a trace can join them.</summary>
        public ModifierSource Source { get; }

        public DamageFlags Flags { get; }

        /// <summary>Who dealt it, or zero for damage with no owner — a hazard, a fall, the world.</summary>
        public uint AttackerNetId { get; }

        public bool Has(DamageFlags flag) => (Flags & flag) != 0;

        /// <summary>The same packet for a different amount. How a hook that scales damage rewrites it.</summary>
        public DamagePacket WithAmount(float amount)
        {
            return new DamagePacket(amount, Source, Flags, AttackerNetId);
        }

        /// <summary>The same packet with extra flags set.</summary>
        public DamagePacket With(DamageFlags flags)
        {
            return new DamagePacket(Amount, Source, Flags | flags, AttackerNetId);
        }

        public override string ToString() => $"{Amount:0.##} [{Flags}] from {Source}";
    }
}
