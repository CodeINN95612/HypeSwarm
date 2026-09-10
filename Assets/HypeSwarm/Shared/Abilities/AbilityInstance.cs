using HypeSwarm.Shared.Cooldowns;
using HypeSwarm.Shared.Stats;

namespace HypeSwarm.Shared.Abilities
{
    /// <summary>
    /// One owner's copy of an ability: its cooldown, its charges, its stacks (§8.1).
    /// </summary>
    /// <remarks>
    /// <b>The whole reason this type exists is that the definition must not hold state.</b> An
    /// <see cref="IAbilityDefinition"/> ships as a ScriptableObject, which is a single shared asset —
    /// two players on the same champion, or one champion with the same ability in two slots, would
    /// share one cooldown timer. That is on the spec's list of traps, and the test suite pins it.
    ///
    /// <para>Plain C#: no Unity types, no clock, no network. Time arrives through
    /// <see cref="Tick"/>, which is what lets the cast state machine be tested in microseconds and
    /// re-simulated at a different rate later.</para>
    /// </remarks>
    public sealed class AbilityInstance
    {
        /// <summary>
        /// Shortest a cooldown may be driven to by haste.
        /// </summary>
        /// <remarks>
        /// Haste derives asymptotically and never reaches one (§5.6.2), so arithmetic alone cannot
        /// produce zero. This is the floor for an ability authored with a tiny cooldown in the first
        /// place: a cooldown below a frame is an ability that fires as fast as the machine allows,
        /// which makes frame rate a damage stat.
        /// </remarks>
        public const float MinimumCooldown = 0.05f;

        readonly ChargePool charges;

        public AbilityInstance(IAbilityDefinition definition, int slot = 0)
        {
            Definition = definition;
            Slot = slot;

            var capacity = definition == null ? 1 : definition.Charges;
            var cooldown = definition == null ? 0f : definition.Cooldown;
            var lockout = definition == null ? 0f : definition.ChargeLockout;

            charges = new ChargePool(capacity < 1 ? 1 : capacity, cooldown, lockout);
        }

        public IAbilityDefinition Definition { get; }

        /// <summary>Which indexed slot this sits in.</summary>
        public int Slot { get; }

        public AbilityRole Role => Definition == null ? AbilityRole.Passive : Definition.Role;

        /// <summary>
        /// What this ability applies things under, so a shield or a slow it put up can be taken off as
        /// a unit (§9).
        /// </summary>
        /// <remarks>
        /// The slot is the instance number, so a champion carrying the same ability twice applies two
        /// separable shields rather than one that refreshes itself.
        /// </remarks>
        public ModifierSource Source =>
            Definition == null ? default : new ModifierSource(Definition.Id, Slot);

        /// <summary>The charges and their timers.</summary>
        public ChargePool Charges => charges;

        /// <summary>
        /// Free-form counter for abilities that build up — a mark count, a stack of heat.
        /// </summary>
        /// <remarks>
        /// On the instance rather than the definition, which is the same rule as the cooldown and for
        /// the same reason. Nothing uses it yet; it is here because the alternative when the first
        /// stacking ability is authored is to add state to the shared asset, which is the trap.
        /// </remarks>
        public int Stacks { get; set; }

        public bool IsReady => charges.CanSpend;

        /// <summary>Seconds until the next charge returns, or zero when one is ready.</summary>
        public float CooldownRemaining => charges.NextChargeIn;

        public int ChargesAvailable => charges.Available;

        public void Tick(float deltaTime) => charges.Tick(deltaTime);

        /// <summary>
        /// Spends a charge and starts its cooldown, or returns false.
        /// </summary>
        /// <remarks>
        /// <b>Haste is read here, at the moment of use, and never again.</b> A cooldown already running
        /// is not shortened by a haste buff that arrives afterwards — which is what League does, what
        /// players expect, and what stops a haste buff being worth more the instant after you spend
        /// everything than the instant before.
        /// </remarks>
        /// <param name="hasteEffect">
        /// The derived haste fraction (<c>sheet.Effect(StatId.Haste)</c>), in <c>(-1, 1)</c>. Passed in
        /// rather than read from a sheet, so this stays free of stats and testable with a number.
        /// </param>
        public bool TryActivate(float hasteEffect)
        {
            if (Definition == null)
            {
                return false;
            }

            charges.Configure(
                Definition.Charges < 1 ? 1 : Definition.Charges,
                CooldownAt(hasteEffect),
                Definition.ChargeLockout);

            return charges.TrySpend();
        }

        /// <summary>
        /// What the cooldown would be at this much haste. For the HUD, and for the arithmetic to be
        /// testable without casting anything.
        /// </summary>
        public float CooldownAt(float hasteEffect)
        {
            if (Definition == null)
            {
                return 0f;
            }

            var cooldown = Definition.Cooldown;

            if (cooldown <= 0f)
            {
                return 0f;
            }

            if (float.IsNaN(hasteEffect))
            {
                hasteEffect = 0f;
            }

            var scaled = cooldown * (1f - hasteEffect);

            return scaled < MinimumCooldown ? MinimumCooldown : scaled;
        }

        /// <summary>Hands back the charge a refused cast spent. For a client the host disagreed with.</summary>
        public bool Refund() => charges.Refund();

        /// <summary>Returns every charge. For respawns and test setup.</summary>
        public void Refresh()
        {
            charges.Refill();
            Stacks = 0;
        }

        public override string ToString()
        {
            return Definition == null ? "empty" : Definition.Id.Value + " (" + Role + ")";
        }
    }
}
