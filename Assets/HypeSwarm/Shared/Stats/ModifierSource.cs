using System;
using HypeSwarm.Shared.Content;

namespace HypeSwarm.Shared.Stats
{
    /// <summary>
    /// Who applied a modifier: what content it came from, and which copy of that content.
    /// </summary>
    /// <remarks>
    /// Every modifier carries one (§9), and it is the whole reason removal is trivial rather than a
    /// bug farm. Unequipping an item, an expiring buff, and losing an augment are all "remove
    /// everything from this source" — no bookkeeping of which modifiers went where, and no chance of
    /// removing a percentage some other effect happened to add.
    ///
    /// <para><b>The instance number is what makes two copies of one item removable separately.</b>
    /// Holding two identical rings and selling one must take away exactly one ring worth of stats;
    /// with the content id alone, either both would go or neither could be told apart. Zero is the
    /// natural instance for anything unique — an augment, a level-up, a status effect.</para>
    ///
    /// <para>A <see cref="ContentId"/> rather than an object reference, because this crosses the
    /// wire (§13.2) and because an object reference would keep an item alive long after it was sold.</para>
    /// </remarks>
    public readonly struct ModifierSource : IEquatable<ModifierSource>
    {
        public ModifierSource(ContentId content, int instance = 0)
        {
            Content = content;
            Instance = instance;
        }

        /// <summary>The content that applied this — <c>item.iron_ring</c>, <c>augment.shield_amp</c>.</summary>
        public ContentId Content { get; }

        /// <summary>Which copy of that content. Zero for anything there is only ever one of.</summary>
        public int Instance { get; }

        /// <summary>False for <c>default(ModifierSource)</c>, which no live modifier should carry.</summary>
        public bool IsValid => Content.IsValid;

        public static ModifierSource Parse(string content, int instance = 0)
        {
            return new ModifierSource(ContentId.Parse(content), instance);
        }

        public bool Equals(ModifierSource other)
        {
            return Content.Equals(other.Content) && Instance == other.Instance;
        }

        public override bool Equals(object obj) => obj is ModifierSource other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (Content.GetHashCode() * 397) ^ Instance;
            }
        }

        public static bool operator ==(ModifierSource left, ModifierSource right) => left.Equals(right);

        public static bool operator !=(ModifierSource left, ModifierSource right) => !left.Equals(right);

        public override string ToString()
        {
            return Instance == 0 ? Content.Value : $"{Content.Value}#{Instance}";
        }
    }
}
