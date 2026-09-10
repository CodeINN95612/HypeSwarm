using System.Collections.Generic;
using HypeSwarm.Shared.Abilities;
using HypeSwarm.Shared.Combat;
using HypeSwarm.Shared.Stats;
using HypeSwarm.Shared.Tuning;

namespace HypeSwarm.Shared.Content
{
    /// <summary>
    /// Checks authored content and tuning for the mistakes that would otherwise surface at runtime,
    /// or — worse — not surface at all.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="ContentLibrary.BuildRegistry"/> on purpose. Building throws on the
    /// first problem, which is right for loading a session and wrong for reviewing your work: an
    /// author with three broken ids wants all three, not one per attempt. So this reports
    /// everything it finds and never throws.
    ///
    /// <para>It lives in Shared rather than an editor assembly because the host will want the same
    /// check on load, and because keeping it free of <c>AssetDatabase</c> is what lets it be tested
    /// without a project. Asset discovery is the editor's job; judging what it found is this.</para>
    /// </remarks>
    public static class ContentValidation
    {
        /// <param name="library">The library that ships, or null if none was found.</param>
        /// <param name="allDefinitionsInProject">
        /// Every <see cref="ContentDefinition"/> that exists, so content authored but never
        /// registered can be reported. That one is invisible at runtime — the asset simply never
        /// loads, and nothing anywhere says why.
        /// </param>
        public static IReadOnlyList<ValidationIssue> ValidateContent(
            ContentLibrary library,
            IReadOnlyList<ContentDefinition> allDefinitionsInProject)
        {
            var issues = new List<ValidationIssue>();

            if (library == null)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "No ContentLibrary asset found. Create one via Assets > Create > Hype Swarm > Content Library — " +
                    "without it no content is registered and nothing resolves."));

                return issues;
            }

            var registered = new HashSet<ContentDefinition>();
            var seenIds = new Dictionary<string, ContentDefinition>();

            for (var i = 0; i < library.Definitions.Count; i++)
            {
                var definition = library.Definitions[i];

                if (definition == null)
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Error,
                        $"Content library slot {i} is empty. Usually a deleted asset — remove the slot or restore it.",
                        library));

                    continue;
                }

                if (!registered.Add(definition))
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Warning,
                        $"'{definition.name}' is listed more than once in the content library.",
                        definition));

                    continue;
                }

                if (!definition.TryValidateId(out var error))
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, $"'{definition.name}': {error}", definition));
                    continue;
                }

                var id = definition.Id.Value;

                if (seenIds.TryGetValue(id, out var owner))
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Error,
                        $"Duplicate content id '{id}': both '{owner.name}' and '{definition.name}' claim it. " +
                        "Ids address content in saves and on the wire, so they must be unique.",
                        definition));

                    continue;
                }

                seenIds.Add(id, definition);
            }

            if (allDefinitionsInProject != null)
            {
                for (var i = 0; i < allDefinitionsInProject.Count; i++)
                {
                    var definition = allDefinitionsInProject[i];

                    if (definition == null || registered.Contains(definition))
                    {
                        continue;
                    }

                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Warning,
                        $"'{definition.name}' is not in the content library, so it will never load. " +
                        "Add it, or delete the asset.",
                        definition));
                }
            }

            return issues;
        }

        /// <summary>
        /// Checks the stat catalog the tuning file produces.
        /// </summary>
        /// <remarks>
        /// <b>Run this before <see cref="ValidateTuning"/>.</b> Building the catalog is what reads
        /// every stat key, and reading a key is what records it as missing — so a stat the config
        /// file forgot only shows up if this has already asked for it.
        /// </remarks>
        public static IReadOnlyList<ValidationIssue> ValidateStats(TuningConfig tuning)
        {
            var issues = new List<ValidationIssue>();
            var catalog = StatCatalog.FromTuning(tuning);

            foreach (var problem in catalog.Validate())
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, problem));
            }

            return issues;
        }

        /// <summary>
        /// The combat numbers that are not stats, read the same way and for the same reason: asking
        /// for a key is what records it as missing.
        /// </summary>
        /// <remarks><b>Run this before <see cref="ValidateTuning"/> too.</b></remarks>
        public static IReadOnlyList<ValidationIssue> ValidateCombat(TuningConfig tuning)
        {
            var issues = new List<ValidationIssue>();
            var problem = CombatSettings.FromTuning(tuning).Validate();

            if (problem != null)
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, problem));
            }

            return issues;
        }

        /// <summary>
        /// Checks every authored ability and champion against the design rules they are easiest to
        /// break by accident.
        /// </summary>
        /// <remarks>
        /// Takes the whole project's content rather than a list of abilities, because the mistakes worth
        /// catching are about what is <i>missing</i> — a champion with no mobility ability, a damage step
        /// with nothing in front of it to say what to damage — and neither is visible from one asset.
        /// </remarks>
        public static IReadOnlyList<ValidationIssue> ValidateAbilities(
            IReadOnlyList<ContentDefinition> definitions,
            AbilitySettings settings)
        {
            var issues = new List<ValidationIssue>();

            if (definitions == null)
            {
                return issues;
            }

            for (var i = 0; i < definitions.Count; i++)
            {
                switch (definitions[i])
                {
                    case AbilityDefinition ability:
                        issues.AddRange(AbilityValidation.Validate(ability, settings, ability));
                        break;

                    case ChampionDefinition champion:
                        issues.AddRange(AbilityValidation.Validate(champion, champion));
                        break;
                }
            }

            return issues;
        }

        /// <summary>
        /// The ability numbers from the tuning file, read the same way and for the same reason: asking
        /// for a key is what records it as missing.
        /// </summary>
        /// <remarks><b>Run this before <see cref="ValidateTuning"/> too.</b></remarks>
        public static IReadOnlyList<ValidationIssue> ValidateAbilitySettings(TuningConfig tuning)
        {
            var issues = new List<ValidationIssue>();
            var problem = AbilitySettings.FromTuning(tuning).Validate();

            if (problem != null)
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, problem));
            }

            return issues;
        }

        /// <param name="loadErrors">Parse and IO problems from <see cref="TuningLoader"/>.</param>
        public static IReadOnlyList<ValidationIssue> ValidateTuning(
            TuningConfig tuning,
            IReadOnlyList<string> loadErrors)
        {
            var issues = new List<ValidationIssue>();

            if (loadErrors != null)
            {
                for (var i = 0; i < loadErrors.Count; i++)
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, loadErrors[i]));
                }
            }

            if (tuning == null)
            {
                return issues;
            }

            if (tuning.Layers.Count == 0)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning,
                    "No tuning files were found, so every value falls back to its hardcoded default."));
            }

            foreach (var entry in tuning.MalformedEntries)
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, entry));
            }

            foreach (var key in tuning.MissingKeys)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning,
                    $"Tuning key '{key}' was read but is not defined anywhere; the caller's fallback was used."));
            }

            return issues;
        }
    }
}
