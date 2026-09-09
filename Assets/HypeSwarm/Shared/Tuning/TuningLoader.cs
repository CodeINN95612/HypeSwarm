using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HypeSwarm.Shared.Tuning
{
    /// <summary>
    /// Builds a <see cref="TuningConfig"/> from <c>.tuning</c> files on disk.
    /// </summary>
    /// <remarks>
    /// Two layers by default (see <see cref="DefaultDirectories"/>):
    /// <list type="number">
    /// <item><description><c>StreamingAssets/Tuning</c> — ships with the build, editable next to the
    /// executable afterwards. This is what "not baked into the build" buys us.</description></item>
    /// <item><description><c>persistentDataPath/Tuning</c> — a local override that survives
    /// reinstalls. Where a designer's in-progress numbers live.</description></item>
    /// </list>
    ///
    /// Within a directory, files load in ordinal filename order, so <c>10-core.tuning</c> loads
    /// before <c>20-waves.tuning</c> and the ordering is visible in the file listing.
    /// </remarks>
    public static class TuningLoader
    {
        public const string DirectoryName = "Tuning";
        public const string FileExtension = "*.tuning";

        /// <summary>
        /// The shipped layer then the local override layer. Neither is required to exist — a
        /// missing directory contributes nothing rather than failing.
        /// </summary>
        public static IReadOnlyList<string> DefaultDirectories()
        {
            return new[]
            {
                Path.Combine(Application.streamingAssetsPath, DirectoryName),
                Path.Combine(Application.persistentDataPath, DirectoryName)
            };
        }

        public static TuningConfig LoadDefaults(out IReadOnlyList<string> errors)
        {
            return Load(DefaultDirectories(), out errors);
        }

        /// <param name="directories">Lowest-priority directory first.</param>
        /// <param name="errors">
        /// Parse and IO problems. Collected rather than thrown: a corrupt override file must not
        /// stop the game from starting on the values it can read.
        /// </param>
        public static TuningConfig Load(IEnumerable<string> directories, out IReadOnlyList<string> errors)
        {
            if (directories == null)
            {
                throw new ArgumentNullException(nameof(directories));
            }

            var sources = new List<ITuningSource>();
            var problems = new List<string>();

            foreach (var directory in directories)
            {
                if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                {
                    continue;
                }

                string[] files;

                try
                {
                    files = Directory.GetFiles(directory, FileExtension);
                }
                catch (Exception exception)
                {
                    problems.Add($"Could not list tuning files in '{directory}': {exception.Message}");
                    continue;
                }

                Array.Sort(files, StringComparer.Ordinal);

                foreach (var file in files)
                {
                    string text;

                    try
                    {
                        text = File.ReadAllText(file);
                    }
                    catch (Exception exception)
                    {
                        problems.Add($"Could not read tuning file '{file}': {exception.Message}");
                        continue;
                    }

                    var source = TuningTextSource.Parse(Path.GetFileName(file), text, out var parseErrors);
                    problems.AddRange(parseErrors);
                    sources.Add(source);
                }
            }

            errors = problems;
            return new TuningConfig(sources);
        }
    }
}
