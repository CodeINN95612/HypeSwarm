using System;

namespace HypeSwarm.Shared.Content
{
    /// <summary>
    /// A stable, human-readable identifier for one piece of content — <c>augment.shield_amp</c>.
    /// </summary>
    /// <remarks>
    /// Content is addressed by this string everywhere it crosses a boundary: save files, the
    /// network protocol, tuning config, and analytics (spec §13.2). Unity GUIDs and direct object
    /// references are never used for those, because they are not readable, not diffable, and not
    /// stable across a re-import.
    ///
    /// <para><b>Once content ships, its id is frozen.</b> Renaming one silently invalidates every
    /// save that referenced it.</para>
    ///
    /// <para>The format is <c>category.name</c>, lowercase, with <c>a-z 0-9 _</c> in each segment
    /// and at least two segments. The restriction is deliberate: the ids are compared, sorted, and
    /// hashed <i>ordinally</i>, so anything that a culture-aware comparison might reorder — case,
    /// accents, punctuation — is simply not allowed to appear.</para>
    /// </remarks>
    public readonly struct ContentId : IEquatable<ContentId>, IComparable<ContentId>
    {
        /// <summary>Segment separator. <c>category.name</c>, and deeper if it helps.</summary>
        public const char Separator = '.';

        /// <summary>Upper bound on id length, so save and network buffers stay bounded.</summary>
        public const int MaxLength = 128;

        readonly string value;

        ContentId(string value)
        {
            this.value = value;
        }

        /// <summary>The id text, or an empty string if this is a default (invalid) id.</summary>
        public string Value => value ?? string.Empty;

        /// <summary>False for <c>default(ContentId)</c> and for anything that failed to parse.</summary>
        public bool IsValid => value != null;

        /// <summary>
        /// The leading segment — <c>augment</c> in <c>augment.shield_amp</c>. Useful for pool
        /// filtering and for grouping content in tooling.
        /// </summary>
        public string Category
        {
            get
            {
                if (value == null)
                {
                    return string.Empty;
                }

                var separator = value.IndexOf(Separator);
                return separator < 0 ? value : value.Substring(0, separator);
            }
        }

        /// <summary>Parses an id, throwing with an explanatory message if it is malformed.</summary>
        public static ContentId Parse(string raw)
        {
            if (!TryParse(raw, out var id, out var error))
            {
                throw new ArgumentException(error, nameof(raw));
            }

            return id;
        }

        /// <summary>Parses an id, returning false rather than throwing if it is malformed.</summary>
        public static bool TryParse(string raw, out ContentId id)
        {
            return TryParse(raw, out id, out _);
        }

        /// <summary>
        /// Parses an id, and on failure explains what is wrong with it. The message is surfaced to
        /// whoever authored the content, so it names the offending character and its position.
        /// </summary>
        public static bool TryParse(string raw, out ContentId id, out string error)
        {
            id = default;

            if (string.IsNullOrEmpty(raw))
            {
                error = "Content id is null or empty.";
                return false;
            }

            if (raw.Length > MaxLength)
            {
                error = $"Content id '{raw}' is {raw.Length} characters long; the maximum is {MaxLength}.";
                return false;
            }

            var segments = 1;
            var segmentLength = 0;

            for (var i = 0; i < raw.Length; i++)
            {
                var c = raw[i];

                if (c == Separator)
                {
                    if (segmentLength == 0)
                    {
                        error = $"Content id '{raw}' has an empty segment at index {i}.";
                        return false;
                    }

                    segments++;
                    segmentLength = 0;
                    continue;
                }

                var allowed = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
                if (!allowed)
                {
                    error = $"Content id '{raw}' contains '{c}' at index {i}; only lowercase a-z, 0-9, '_' and '{Separator}' are allowed.";
                    return false;
                }

                segmentLength++;
            }

            if (segmentLength == 0)
            {
                error = $"Content id '{raw}' ends with '{Separator}'.";
                return false;
            }

            if (segments < 2)
            {
                error = $"Content id '{raw}' needs a category prefix — try 'augment.{raw}' or 'item.{raw}'.";
                return false;
            }

            id = new ContentId(raw);
            error = null;
            return true;
        }

        public bool Equals(ContentId other)
        {
            return string.Equals(value, other.value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is ContentId other && Equals(other);
        }

        public override int GetHashCode()
        {
            // In-process dictionary bucketing only. Anything persisted or networked must use
            // Fnv1a instead — string.GetHashCode is randomised per process and per runtime.
            return value == null ? 0 : StringComparer.Ordinal.GetHashCode(value);
        }

        /// <summary>
        /// Ordinal comparison. This is load-bearing: network indices are assigned from sorted order
        /// (<see cref="ContentRegistry.Seal"/>), so a culture-sensitive sort would hand two machines
        /// in different locales two different index tables for identical content.
        /// </summary>
        public int CompareTo(ContentId other)
        {
            return string.CompareOrdinal(value, other.value);
        }

        public override string ToString()
        {
            return Value;
        }

        public static bool operator ==(ContentId left, ContentId right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(ContentId left, ContentId right)
        {
            return !left.Equals(right);
        }
    }
}
