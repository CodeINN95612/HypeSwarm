using System.Collections.Generic;

namespace HypeSwarm.Shared.Tuning
{
    /// <summary>
    /// One layer of tuning values. <see cref="TuningConfig"/> stacks these, later layers winning.
    /// </summary>
    /// <remarks>
    /// The interface exists so the storage format stays swappable. Today it is a flat text file
    /// (<see cref="TuningTextSource"/>); if curves outgrow that, a JSON or binary source drops in
    /// without touching a single call site.
    /// </remarks>
    public interface ITuningSource
    {
        /// <summary>Where these values came from — a file path or a label. Used in error messages.</summary>
        string Name { get; }

        bool TryGetValue(string key, out string value);

        IEnumerable<string> Keys { get; }
    }
}
