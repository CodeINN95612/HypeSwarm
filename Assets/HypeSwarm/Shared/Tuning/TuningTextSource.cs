using System;
using System.Collections.Generic;

namespace HypeSwarm.Shared.Tuning
{
    /// <summary>
    /// A tuning layer parsed from flat <c>key = value</c> text.
    /// </summary>
    /// <remarks>
    /// <code>
    /// # Wave director
    /// wave.density.start      = 12
    /// wave.density.per_minute = 3.5
    /// wave.elite.first_at     = 90
    /// siege.structure.health  = 4000, 6000, 9000
    /// </code>
    ///
    /// Deliberately not JSON. Tuning is a flat namespace of numbers edited constantly by hand and
    /// reviewed in diffs, and this format has no punctuation to get wrong at 2am, one value per
    /// line, and comments — none of which JSON offers. <see cref="ITuningSource"/> is the seam that
    /// keeps a richer format available if curves ever need one.
    /// </remarks>
    public sealed class TuningTextSource : ITuningSource
    {
        public const char CommentChar = '#';
        public const char AssignChar = '=';

        readonly Dictionary<string, string> values;

        TuningTextSource(string name, Dictionary<string, string> values)
        {
            Name = name;
            this.values = values;
        }

        public string Name { get; }

        public IEnumerable<string> Keys => values.Keys;

        public int Count => values.Count;

        /// <summary>
        /// Parses tuning text. Malformed lines are collected in <paramref name="errors"/> rather
        /// than thrown: one typo should cost you that one value, not the whole file — but it must
        /// still be reported loudly, because a silently missing constant becomes a balance mystery.
        /// </summary>
        public static TuningTextSource Parse(string name, string text, out IReadOnlyList<string> errors)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var problems = new List<string>();

            if (!string.IsNullOrEmpty(text))
            {
                var lines = text.Split('\n');

                for (var i = 0; i < lines.Length; i++)
                {
                    ParseLine(name, lines[i], i + 1, values, problems);
                }
            }

            errors = problems;
            return new TuningTextSource(name, values);
        }

        public bool TryGetValue(string key, out string value)
        {
            return values.TryGetValue(key, out value);
        }

        static void ParseLine(
            string sourceName,
            string rawLine,
            int lineNumber,
            Dictionary<string, string> values,
            List<string> problems)
        {
            var line = rawLine.Trim();

            if (line.Length == 0 || line[0] == CommentChar)
            {
                return;
            }

            var assign = line.IndexOf(AssignChar);

            if (assign < 0)
            {
                problems.Add($"{sourceName}:{lineNumber}: expected 'key {AssignChar} value', got '{line}'.");
                return;
            }

            var key = line.Substring(0, assign).Trim();
            var value = line.Substring(assign + 1).Trim();

            if (key.Length == 0)
            {
                problems.Add($"{sourceName}:{lineNumber}: missing key before '{AssignChar}'.");
                return;
            }

            if (value.Length == 0)
            {
                problems.Add($"{sourceName}:{lineNumber}: key '{key}' has no value.");
                return;
            }

            if (values.ContainsKey(key))
            {
                // Within one file this is always a mistake — the later line silently wins and the
                // earlier edit appears to have done nothing. Across files it is the point, and
                // TuningConfig handles it there.
                problems.Add($"{sourceName}:{lineNumber}: '{key}' is set more than once in this file.");
            }

            values[key] = value;
        }
    }
}
