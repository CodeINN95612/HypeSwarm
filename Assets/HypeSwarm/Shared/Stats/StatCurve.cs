namespace HypeSwarm.Shared.Stats
{
    /// <summary>
    /// Turns a linear, unbounded stat into a bounded effect: <c>effect = value / (value + K)</c>.
    /// </summary>
    /// <remarks>
    /// This is the whole of spec §5.6.2 and it exists to make one rule enforceable: <b>never cap a
    /// stat.</b> Damage reduction, cooldown, dodge and lifesteal all break the game at 100%, and the
    /// obvious fix — clamping the stat — produces items that are worth nothing once you own three of
    /// them, which players notice and resent long before a designer does.
    ///
    /// <para>Deriving instead keeps the player-facing number linear and always worth buying. A fixed
    /// amount of damage reduction raises effective health by the same <i>proportion</i> forever, even
    /// though the percentage curve flattens. The number goes up, it keeps mattering, and the game
    /// never divides by zero.</para>
    ///
    /// <para><b>Negative values mirror the curve rather than falling off it.</b> A vulnerability
    /// debuff that pushed damage reduction to <c>-K</c> would divide by zero on the naive formula;
    /// here the sign is taken out first and put back afterwards, so the result is symmetric and stays
    /// strictly inside <c>(-1, 1)</c> at every input, including absurd ones.</para>
    /// </remarks>
    public static class StatCurve
    {
        /// <summary>
        /// The largest float below one. What an infinite stat derives to — the guarantee is that the
        /// effect never <i>reaches</i> one, and at these magnitudes the division would round to
        /// exactly one and hand the caller an immunity.
        /// </summary>
        public const float LargestFractionBelowOne = 0.99999994f;

        /// <summary>
        /// Floor for K. A soft cap of zero would make every point of the stat worth an entire
        /// asymptote, which is a configuration mistake rather than a design; clamped instead of
        /// thrown so a bad tuning file cannot stop a build from running.
        /// <see cref="StatCatalog.Validate"/> is what reports it.
        /// </summary>
        public const float MinimumSoftCap = 0.0001f;

        /// <summary>
        /// The bounded effect of a stat, in <c>(-1, 1)</c>. Half of it at <c>value == softCap</c>,
        /// which is what makes K readable: it is the amount that buys 50%.
        /// </summary>
        public static float Diminishing(float value, float softCap)
        {
            if (float.IsNaN(value))
            {
                return 0f;
            }

            var k = softCap < MinimumSoftCap ? MinimumSoftCap : softCap;
            var magnitude = value < 0f ? -value : value;
            var fraction = magnitude / (magnitude + k);

            // Infinity divided by infinity is NaN, and merely enormous values round to exactly one.
            if (float.IsNaN(fraction) || fraction >= 1f)
            {
                fraction = LargestFractionBelowOne;
            }

            return value < 0f ? -fraction : fraction;
        }

        /// <summary>
        /// What incoming damage is multiplied by at this much damage reduction. Approaches zero and
        /// never reaches it; above one when the stat is negative, which is how vulnerability works.
        /// </summary>
        public static float Reduction(float value, float softCap)
        {
            return 1f - Diminishing(value, softCap);
        }

        /// <summary>
        /// The stat value that produces a given effect — the inverse of <see cref="Diminishing"/>.
        /// For the UI: "how much haste do I need for 40%" is a question players ask constantly, and
        /// answering it by search would be embarrassing.
        /// </summary>
        public static float Required(float effect, float softCap)
        {
            var k = softCap < MinimumSoftCap ? MinimumSoftCap : softCap;
            var magnitude = effect < 0f ? -effect : effect;

            if (magnitude >= 1f || float.IsNaN(magnitude))
            {
                return effect < 0f ? float.NegativeInfinity : float.PositiveInfinity;
            }

            var value = k * magnitude / (1f - magnitude);

            return effect < 0f ? -value : value;
        }
    }
}
