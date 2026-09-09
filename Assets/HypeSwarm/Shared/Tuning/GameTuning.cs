using HypeSwarm.Shared.Stats;
using UnityEngine;

namespace HypeSwarm.Shared.Tuning
{
    /// <summary>
    /// The tuning the running game reads, loaded once from disk on first use.
    /// </summary>
    /// <remarks>
    /// One place, so that a value coming from the config file rather than from a compiled constant
    /// is a property of the whole session rather than of whichever component happened to load it
    /// (§13.3). Anything that wants a balance number goes through here.
    ///
    /// <para><b>Statics survive play mode in this project</b>, which enters play without a domain
    /// reload. Left alone, the first Play of an Editor session would read the config and every Play
    /// after it would quietly reuse it — so editing a number and pressing Play would appear to do
    /// nothing, which is the exact opposite of what external tuning is for. The reset below is what
    /// makes each Play a fresh read.</para>
    /// </remarks>
    public static class GameTuning
    {
        static TuningConfig config;
        static StatCatalog stats;

        /// <summary>Every tuning value, shipped layer under local override.</summary>
        public static TuningConfig Config => config ??= Load();

        /// <summary>The stat definitions with the tuning file laid over the compiled defaults.</summary>
        public static StatCatalog Stats => stats ??= BuildStats();

        /// <summary>Drops what was loaded so the next read comes off disk again.</summary>
        public static void Reload()
        {
            config = null;
            stats = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlayMode() => Reload();

        static TuningConfig Load()
        {
            var loaded = TuningLoader.LoadDefaults(out var errors);

            for (var i = 0; i < errors.Count; i++)
            {
                // A bad config file must not stop the game starting, but it must not be silent
                // either — a typo that falls back to a default is a balance bug that takes days.
                Debug.LogWarning($"[Tuning] {errors[i]}");
            }

            return loaded;
        }

        static StatCatalog BuildStats()
        {
            var catalog = StatCatalog.FromTuning(Config);
            var problems = catalog.Validate();

            for (var i = 0; i < problems.Count; i++)
            {
                Debug.LogWarning($"[Tuning] {problems[i]}");
            }

            return catalog;
        }
    }
}
