using System;

namespace HypeSwarm.Shared.Stats
{
    /// <summary>
    /// One change to one stat, from one source. The unit the stat system is built out of, and the
    /// unit that travels over the network — values are never replicated, modifier lists are.
    /// </summary>
    /// <remarks>
    /// Immutable, because a modifier that could be edited in place would be a modifier whose removal
    /// no longer matches what was applied. Changing a buff means removing it and adding another.
    /// </remarks>
    public readonly struct StatModifier : IEquatable<StatModifier>
    {
        public StatModifier(StatId stat, ModifierOperation operation, float value, ModifierSource source)
        {
            Stat = stat;
            Operation = operation;
            Value = value;
            Source = source;
        }

        public StatId Stat { get; }

        public ModifierOperation Operation { get; }

        /// <summary>
        /// Flat amount, or a fraction for the percentage operations — <c>0.15f</c> is fifteen
        /// percent, not fifteen. Percentages are fractions everywhere and become a "15%" readout
        /// only at the UI, so no formula in the codebase divides by a hundred.
        /// </summary>
        public float Value { get; }

        public ModifierSource Source { get; }

        public static StatModifier Flat(StatId stat, float value, ModifierSource source)
        {
            return new StatModifier(stat, ModifierOperation.FlatAdd, value, source);
        }

        public static StatModifier PercentAdd(StatId stat, float fraction, ModifierSource source)
        {
            return new StatModifier(stat, ModifierOperation.PercentAdd, fraction, source);
        }

        public static StatModifier PercentMult(StatId stat, float fraction, ModifierSource source)
        {
            return new StatModifier(stat, ModifierOperation.PercentMult, fraction, source);
        }

        /// <summary>The same modifier under a different source. For copying an effect onto an ally.</summary>
        public StatModifier From(ModifierSource source)
        {
            return new StatModifier(Stat, Operation, Value, source);
        }

        public bool Equals(StatModifier other)
        {
            return Stat == other.Stat
                   && Operation == other.Operation
                   && Value.Equals(other.Value)
                   && Source.Equals(other.Source);
        }

        public override bool Equals(object obj) => obj is StatModifier other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (int)Stat;
                hash = (hash * 397) ^ (int)Operation;
                hash = (hash * 397) ^ Value.GetHashCode();
                hash = (hash * 397) ^ Source.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(StatModifier left, StatModifier right) => left.Equals(right);

        public static bool operator !=(StatModifier left, StatModifier right) => !left.Equals(right);

        public override string ToString()
        {
            var amount = Operation == ModifierOperation.FlatAdd
                ? Value.ToString("+0.##;-0.##;0")
                : (Value * 100f).ToString("+0.##;-0.##;0") + "%";

            var kind = Operation == ModifierOperation.PercentMult ? " (mult)" : string.Empty;

            return $"{Stat} {amount}{kind} from {Source}";
        }
    }
}
