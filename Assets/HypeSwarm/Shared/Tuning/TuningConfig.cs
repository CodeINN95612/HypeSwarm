using System;
using System.Collections.Generic;
using System.Globalization;

namespace HypeSwarm.Shared.Tuning
{
    /// <summary>
    /// Difficulty curves and balance constants, read from external config rather than baked into
    /// the build (spec §13.3).
    /// </summary>
    /// <remarks>
    /// Layered: later sources win, so compiled defaults sit underneath a shipped config file,
    /// which sits underneath a local override. Tuning a number does not require a rebuild.
    ///
    /// <para><b>Every value is parsed with <see cref="CultureInfo.InvariantCulture"/>.</b> Under a
    /// locale where the decimal separator is a comma, a default-culture parse reads
    /// <c>"1.5"</c> as <c>15</c> — a config file that is correct on one machine and silently wrong
    /// on another. There is a test pinning this.</para>
    ///
    /// <para>A miss returns the caller's fallback and is recorded in <see cref="MissingKeys"/>; an
    /// unparseable value does the same and is recorded in <see cref="MalformedEntries"/>. Neither
    /// throws, because a shipped build must survive a bad config file — but neither is silent
    /// either, since a typo that quietly falls back to a default is a balance bug that takes days
    /// to find. Call <see cref="Validate"/> at startup to surface both.</para>
    /// </remarks>
    public sealed class TuningConfig
    {
        readonly List<ITuningSource> layers;
        readonly HashSet<string> missingKeys = new HashSet<string>(StringComparer.Ordinal);
        readonly List<string> malformedEntries = new List<string>();

        public TuningConfig(params ITuningSource[] sources)
            : this((IEnumerable<ITuningSource>)sources)
        {
        }

        /// <param name="sources">Lowest-priority layer first.</param>
        public TuningConfig(IEnumerable<ITuningSource> sources)
        {
            layers = new List<ITuningSource>();

            if (sources == null)
            {
                return;
            }

            foreach (var source in sources)
            {
                if (source == null)
                {
                    throw new ArgumentException("Tuning sources may not be null.", nameof(sources));
                }

                layers.Add(source);
            }
        }

        /// <summary>The layers, lowest priority first.</summary>
        public IReadOnlyList<ITuningSource> Layers => layers;

        /// <summary>Keys that were asked for and not found. Almost always typos.</summary>
        public IReadOnlyCollection<string> MissingKeys => missingKeys;

        /// <summary>Values that were found but could not be parsed as the requested type.</summary>
        public IReadOnlyList<string> MalformedEntries => malformedEntries;

        /// <summary>Every key defined by any layer.</summary>
        public IReadOnlyCollection<string> AllKeys
        {
            get
            {
                var keys = new HashSet<string>(StringComparer.Ordinal);

                for (var i = 0; i < layers.Count; i++)
                {
                    foreach (var key in layers[i].Keys)
                    {
                        keys.Add(key);
                    }
                }

                return keys;
            }
        }

        /// <summary>
        /// Reads the raw text for a key from the highest-priority layer that defines it.
        /// </summary>
        public bool TryGetRaw(string key, out string value, out string sourceName)
        {
            for (var i = layers.Count - 1; i >= 0; i--)
            {
                if (layers[i].TryGetValue(key, out value))
                {
                    sourceName = layers[i].Name;
                    return true;
                }
            }

            value = null;
            sourceName = null;
            return false;
        }

        public bool TryGetFloat(string key, out float value)
        {
            value = 0f;

            if (!TryGetRaw(key, out var raw, out var source))
            {
                missingKeys.Add(key);
                return false;
            }

            return TryParseFloat(key, raw, source, out value);
        }

        public float GetFloat(string key, float fallback)
        {
            return TryGetFloat(key, out var value) ? value : fallback;
        }

        public bool TryGetInt(string key, out int value)
        {
            value = 0;

            if (!TryGetRaw(key, out var raw, out var source))
            {
                missingKeys.Add(key);
                return false;
            }

            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }

            malformedEntries.Add($"{source}: '{key}' = '{raw}' is not a whole number.");
            return false;
        }

        public int GetInt(string key, int fallback)
        {
            return TryGetInt(key, out var value) ? value : fallback;
        }

        public bool TryGetBool(string key, out bool value)
        {
            value = false;

            if (!TryGetRaw(key, out var raw, out var source))
            {
                missingKeys.Add(key);
                return false;
            }

            if (string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase) || raw == "1")
            {
                value = true;
                return true;
            }

            if (string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase) || raw == "0")
            {
                value = false;
                return true;
            }

            malformedEntries.Add($"{source}: '{key}' = '{raw}' is not true/false.");
            return false;
        }

        public bool GetBool(string key, bool fallback)
        {
            return TryGetBool(key, out var value) ? value : fallback;
        }

        /// <summary>
        /// Reads a comma-separated list of numbers — the shape difficulty curves and per-tier
        /// values take. A single malformed entry rejects the whole list rather than silently
        /// shortening it.
        /// </summary>
        public bool TryGetFloats(string key, out float[] values)
        {
            values = null;

            if (!TryGetRaw(key, out var raw, out var source))
            {
                missingKeys.Add(key);
                return false;
            }

            var parts = raw.Split(',');
            var parsed = new float[parts.Length];

            for (var i = 0; i < parts.Length; i++)
            {
                if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed[i]))
                {
                    malformedEntries.Add($"{source}: '{key}' entry {i} is '{parts[i].Trim()}', which is not a number.");
                    return false;
                }
            }

            values = parsed;
            return true;
        }

        public float[] GetFloats(string key, float[] fallback)
        {
            return TryGetFloats(key, out var values) ? values : fallback;
        }

        /// <summary>
        /// Everything that went wrong reading this config so far — missing keys and unparseable
        /// values. Log it at startup; an empty report is the only acceptable shipping state.
        /// </summary>
        public IReadOnlyList<string> Validate()
        {
            var report = new List<string>(malformedEntries);

            foreach (var key in missingKeys)
            {
                report.Add($"Missing tuning key '{key}' — the caller's fallback was used.");
            }

            return report;
        }

        bool TryParseFloat(string key, string raw, string source, out float value)
        {
            if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }

            malformedEntries.Add($"{source}: '{key}' = '{raw}' is not a number.");
            return false;
        }
    }
}
