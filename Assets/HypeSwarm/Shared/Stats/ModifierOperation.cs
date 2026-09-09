namespace HypeSwarm.Shared.Stats
{
    /// <summary>
    /// The three ways a modifier can change a stat, in the order they are applied.
    /// </summary>
    /// <remarks>
    /// The order is the one the spec fixes once and does not renegotiate (§9):
    ///
    /// <code>(base + flatAdd) × (1 + Σ percentAdd) × Π (1 + percentMult)</code>
    ///
    /// <b>The numeric values are the application order</b>, and <see cref="StatSheet"/> relies on
    /// that: flats before percentages, additive percentages before multiplicative ones. This is what
    /// makes the order modifiers arrive in irrelevant — five augments picked in any sequence produce
    /// the same sheet, which is an invariant with a test on it.
    ///
    /// <para>Getting this wrong is not a bug that shows up as a crash. It shows up as every item in
    /// the game needing to be rebalanced eighteen months later.</para>
    /// </remarks>
    public enum ModifierOperation
    {
        /// <summary>Adds to the base value. <c>+40 max health.</c></summary>
        FlatAdd = 0,

        /// <summary>
        /// Adds a fraction that stacks additively with every other one. <c>+15% damage.</c> Two of
        /// these give +30%, not +32.25%.
        /// </summary>
        PercentAdd = 1,

        /// <summary>
        /// Multiplies, stacking multiplicatively with every other one. <c>×1.15 damage.</c> Rare on
        /// purpose: this is the operation that compounds, so it belongs to run-defining effects
        /// rather than to items.
        /// </summary>
        PercentMult = 2
    }
}
