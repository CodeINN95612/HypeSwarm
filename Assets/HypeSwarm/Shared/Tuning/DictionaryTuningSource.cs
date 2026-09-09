using System;
using System.Collections.Generic;

namespace HypeSwarm.Shared.Tuning
{
    /// <summary>
    /// An in-memory tuning layer. Used for compiled-in defaults, for runtime overrides from a debug
    /// console, and as the fixture type in tests.
    /// </summary>
    public sealed class DictionaryTuningSource : ITuningSource
    {
        readonly Dictionary<string, string> values;

        public DictionaryTuningSource(string name, IEnumerable<KeyValuePair<string, string>> entries = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            values = new Dictionary<string, string>(StringComparer.Ordinal);

            if (entries == null)
            {
                return;
            }

            foreach (var entry in entries)
            {
                values[entry.Key] = entry.Value;
            }
        }

        public string Name { get; }

        public IEnumerable<string> Keys => values.Keys;

        public int Count => values.Count;

        public DictionaryTuningSource Set(string key, string value)
        {
            values[key] = value;
            return this;
        }

        public bool TryGetValue(string key, out string value)
        {
            return values.TryGetValue(key, out value);
        }
    }
}
