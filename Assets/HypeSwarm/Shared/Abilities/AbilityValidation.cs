using System.Collections.Generic;
using HypeSwarm.Shared.Content;

namespace HypeSwarm.Shared.Abilities
{
    /// <summary>
    /// The authoring mistakes an ability or a champion can make, reported rather than corrected.
    /// </summary>
    /// <remarks>
    /// Several of the rules here are design rules the spec states as prose — mobility abilities are
    /// always free-cast, rooted casts stay short, every champion has all five roles. Prose is not
    /// enforcement: the whole value of authoring content in the inspector is that someone who is not
    /// reading this file can do it, and the rules that matter most are the ones they will break by
    /// accident.
    ///
    /// <para><b>Nothing here changes anything.</b> An ability with a two-second root is a balance bug
    /// the author has to see; quietly clamping it to the ceiling would produce an ability that does not
    /// do what its asset says, which is worse than one that is wrong.</para>
    ///
    /// <para>Works against the interfaces rather than the asset types, so every rule is tested with a
    /// plain C# object and no project.</para>
    /// </remarks>
    public static class AbilityValidation
    {
        /// <summary>Everything wrong with one ability.</summary>
        /// <param name="context">The asset to select when the author clicks the issue, if there is one.</param>
        public static IReadOnlyList<ValidationIssue> Validate(
            IAbilityDefinition ability,
            AbilitySettings settings,
            UnityEngine.Object context = null)
        {
            var issues = new List<ValidationIssue>();

            if (ability == null)
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, "An empty ability slot.", context));

                return issues;
            }

            var name = Describe(ability);

            ValidateCastCost(ability, settings, context, name, issues);
            ValidateCooldown(ability, context, name, issues);
            ValidateTargeting(ability, context, name, issues);
            ValidateSteps(ability.StartSteps, "start step", ability, context, name, issues);
            ValidateSteps(ability.Steps, "step", ability, context, name, issues);

            return issues;
        }

        /// <summary>Everything wrong with one champion's five slots.</summary>
        public static IReadOnlyList<ValidationIssue> Validate(
            IChampionDefinition champion,
            UnityEngine.Object context = null)
        {
            var issues = new List<ValidationIssue>();

            if (champion == null)
            {
                return issues;
            }

            var name = Describe(champion);
            var abilities = champion.Abilities;

            if (abilities == null || abilities.Count == 0)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    $"{name} has no abilities, so it cannot do anything at all.",
                    context));

                return issues;
            }

            var roles = new Dictionary<AbilityRole, IAbilityDefinition>();

            for (var i = 0; i < abilities.Count; i++)
            {
                var ability = abilities[i];

                if (ability == null)
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Error,
                        $"{name} slot {i} is empty.",
                        context));

                    continue;
                }

                if (roles.TryGetValue(ability.Role, out var owner))
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Error,
                        $"{name} has two abilities in the {ability.Role} role: " +
                        $"'{Describe(owner)}' and '{Describe(ability)}'. Augments target the role, so " +
                        "two of one role means one of them is unreachable.",
                        context));

                    continue;
                }

                roles.Add(ability.Role, ability);
            }

            for (var i = 0; i < AbilityRoles.All.Length; i++)
            {
                var role = AbilityRoles.All[i];

                if (roles.ContainsKey(role))
                {
                    continue;
                }

                // Mobility gets its own sentence because its absence is on the trap list: with no
                // basic attacks, every bit of spacing a champion has comes from movement.
                var why = role == AbilityRole.Mobility
                    ? " With no basic attacks, a champion without mobility has no way to create distance."
                    : string.Empty;

                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    $"{name} has no {role} ability. Every champion has all five (§5.5.3).{why}",
                    context));
            }

            if (abilities.Count != AbilityRoles.Count)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning,
                    $"{name} has {abilities.Count} abilities; the layout is {AbilityRoles.Count} — " +
                    "passive, primary, secondary, mobility, ultimate.",
                    context));
            }

            return issues;
        }

        static void ValidateCastCost(
            IAbilityDefinition ability,
            AbilitySettings settings,
            UnityEngine.Object context,
            string name,
            List<ValidationIssue> issues)
        {
            if (ability.Role == AbilityRole.Mobility && ability.CastCost != CastCost.Free)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    $"{name} is a mobility ability that casts {ability.CastCost}. Mobility is always " +
                    "free-cast (§5.5.4) or the slot cannot serve its escape function.",
                    context));
            }

            if (ability.Role == AbilityRole.Passive)
            {
                if (ability.CastTime > 0f || ability.CastCost != CastCost.Free)
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Error,
                        $"{name} is a passive with a cast cost. A passive is never cast — it pulses on " +
                        "a tick and takes no input (§5.5.3).",
                        context));
                }

                if (ability.Cooldown > 0f)
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Warning,
                        $"{name} is a passive with a cooldown, which nothing reads. Its rate is the " +
                        $"tuned pulse interval ({AbilitySettings.PassiveTickIntervalKey}).",
                        context));
                }
            }

            if (ability.CastCost == CastCost.Rooted && ability.CastTime > settings.RootedCastCeiling)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    $"{name} roots for {ability.CastTime}s, over the {settings.RootedCastCeiling}s " +
                    "ceiling. In a dense horde a long root is a death sentence, not a decision (§5.5.4).",
                    context));
            }

            if (ability.CastCost == CastCost.Channelled && ability.MaxChannelDuration <= 0f)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    $"{name} is channelled but has no maximum channel duration, so it would never " +
                    "resolve on its own.",
                    context));
            }

            if (ability.CastCost != CastCost.Channelled && ability.MaxChannelDuration > 0f)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning,
                    $"{name} authors a channel duration but does not channel, so the number is ignored.",
                    context));
            }
        }

        static void ValidateCooldown(
            IAbilityDefinition ability,
            UnityEngine.Object context,
            string name,
            List<ValidationIssue> issues)
        {
            if (ability.Cooldown < 0f)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    $"{name} has a negative cooldown.",
                    context));
            }

            if (ability.Role != AbilityRole.Passive && ability.Charges < 1)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    $"{name} has {ability.Charges} charges, so it can never be cast.",
                    context));
            }

            if (ability.ChargeLockout < 0f)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    $"{name} has a negative charge lockout.",
                    context));
            }

            if (ability.Charges > 1 && ability.ChargeLockout <= 0f)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning,
                    $"{name} has {ability.Charges} charges and no lockout between them, so one frame " +
                    "of input can spend two. Players read that as the game eating an input (§5.5.3).",
                    context));
            }
        }

        static void ValidateTargeting(
            IAbilityDefinition ability,
            UnityEngine.Object context,
            string name,
            List<ValidationIssue> issues)
        {
            if (ability.Range < 0f)
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, $"{name} has a negative range.", context));
            }

            if (ability.Targeting == TargetingMode.AimPoint && ability.Range <= 0f)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    $"{name} aims at a point but has no range, so every cast lands on the caster.",
                    context));
            }

            if (ability.Targeting == TargetingMode.Self && ability.Range > 0f)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Info,
                    $"{name} is self-targeted, so its range is never read.",
                    context));
            }
        }

        static void ValidateSteps(
            IReadOnlyList<IEffectStep> steps,
            string label,
            IAbilityDefinition ability,
            UnityEngine.Object context,
            string name,
            List<ValidationIssue> issues)
        {
            if (steps == null || steps.Count == 0)
            {
                // Start steps are empty on almost every ability, and that is the normal case rather
                // than a mistake. An ability with no steps at all is the one worth reporting.
                if (label == "step" && (ability.StartSteps == null || ability.StartSteps.Count == 0))
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Warning,
                        $"{name} has no effect steps, so casting it does nothing.",
                        context));
                }

                return;
            }

            var provided = false;

            for (var i = 0; i < steps.Count; i++)
            {
                var step = steps[i];

                if (step == null)
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Error,
                        $"{name} {label} {i} is empty. Usually a deleted asset.",
                        context));

                    continue;
                }

                // The commonest authoring mistake by a distance: a damage step with nothing in front
                // of it to say what to damage. It fails silently — the ability casts, the cooldown
                // starts, and nothing takes a point of damage.
                if (step.NeedsTargets && !provided)
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Error,
                        $"{name} {label} {i} needs targets but nothing before it selects any, so it will " +
                        "affect nothing. Put a targeting step in front of it (§8.2).",
                        context));
                }

                if (step.ProvidesTargets)
                {
                    provided = true;
                }

                if (!step.RunsOnAuthority && !step.RunsOnPrediction)
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Warning,
                        $"{name} {label} {i} runs on neither the host nor the casting client, so it never runs.",
                        context));
                }
            }
        }

        static string Describe(IContentDefinition definition)
        {
            if (definition == null)
            {
                return "an empty slot";
            }

            var id = definition.Id.Value;

            return string.IsNullOrEmpty(id) ? "an ability with no id" : id;
        }
    }
}
