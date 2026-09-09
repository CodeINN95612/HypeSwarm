using UnityEngine;

namespace HypeSwarm.Shared.Content
{
    public enum ValidationSeverity
    {
        /// <summary>Worth knowing, not worth fixing.</summary>
        Info,

        /// <summary>Loads, but almost certainly not what the author meant.</summary>
        Warning,

        /// <summary>Will not load. Fix before running.</summary>
        Error
    }

    /// <summary>
    /// One finding from a validation pass, with the asset it concerns so tooling can select it.
    /// </summary>
    public readonly struct ValidationIssue
    {
        public ValidationIssue(ValidationSeverity severity, string message, Object context = null)
        {
            Severity = severity;
            Message = message;
            Context = context;
        }

        public ValidationSeverity Severity { get; }

        public string Message { get; }

        /// <summary>The offending asset, when there is one. Null for project-wide findings.</summary>
        public Object Context { get; }

        public override string ToString()
        {
            return $"{Severity}: {Message}";
        }
    }
}
