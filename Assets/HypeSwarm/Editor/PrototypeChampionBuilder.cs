using System.Collections.Generic;
using System.IO;
using HypeSwarm.Shared.Abilities;
using HypeSwarm.Shared.Abilities.Steps;
using HypeSwarm.Shared.Combat;
using HypeSwarm.Shared.Content;
using HypeSwarm.Shared.Stats;
using UnityEditor;
using UnityEngine;

namespace HypeSwarm.Editor
{
    /// <summary>
    /// Builds the prototype champion — five abilities and the effect steps behind them — as assets.
    /// </summary>
    /// <remarks>
    /// <b>Nothing here is code the ability system runs.</b> Every asset this produces is data that could
    /// have been made by right-clicking in the Project window and filling in the inspector, which is the
    /// phase exit criterion; this exists so that the champion is reproducible, reviewable as a diff, and
    /// rebuildable after a field is renamed — the same reasoning as
    /// <see cref="PrototypeArenaBuilder"/> for scenes and prefabs.
    ///
    /// <para>The champion is a real design rather than a test fixture, because a placeholder would not
    /// exercise the thing being proved. Between the five slots it covers every claim this phase step
    /// makes: all four cast costs, a step function, a coefficient against a defensive stat, a channel
    /// that pays off in proportion to commitment, charges with a lockout, and a mobility ability that
    /// does something besides move.</para>
    ///
    /// <para>Rerunning replaces the assets it owns, keeping their GUIDs so references survive.</para>
    /// </remarks>
    public static class PrototypeChampionBuilder
    {
        const string ChampionFolder = "Assets/HypeSwarm/Content/Champions";
        const string AbilityFolder = "Assets/HypeSwarm/Content/Abilities";
        const string StepFolder = "Assets/HypeSwarm/Content/EffectSteps";

        /// <summary>
        /// Reach of the passive aura, in metres.
        /// </summary>
        /// <remarks>
        /// Radii are authored per ability and never scaled by a stat (§5.5.5). These constants are
        /// therefore balance numbers rather than tuning ones: they live on the asset, and this file is how
        /// the asset gets its first value.
        /// </remarks>
        const float CindersRadius = 4.5f;

        const float LanceRange = 9f;
        const float LanceCone = 55f;
        const float WardRadius = 5.5f;
        const float DashRadius = 3.5f;
        const float CrucibleRadius = 7f;

        [MenuItem("Hype Swarm/Build Prototype Champion")]
        public static void BuildFromMenu()
        {
            if (EditorUtility.DisplayDialog(
                    "Build prototype champion",
                    "This replaces the ability, effect step and champion assets for Pyre.\n\n" +
                    "Any hand edits to them are lost.",
                    "Rebuild",
                    "Cancel"))
            {
                Build();
            }
        }

        /// <summary>Rebuilds without asking, so it can be driven from the command line.</summary>
        public static ChampionDefinition Build()
        {
            EnsureFolder(ChampionFolder);
            EnsureFolder(AbilityFolder);
            EnsureFolder(StepFolder);

            var created = new List<ContentDefinition>();

            var champion = BuildChampion(created);

            Register(created);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"Prototype champion rebuilt: {created.Count} assets. " +
                      "Hype Swarm > Validate Content checks them.");

            return champion;
        }

        static ChampionDefinition BuildChampion(List<ContentDefinition> created)
        {
            var abilities = new[]
            {
                Cinders(created),
                Lance(created),
                Ward(created),
                Dash(created),
                Crucible(created)
            };

            var champion = Asset<ChampionDefinition>(ChampionFolder, "Pyre", "champion.pyre", created);

            Write(champion, new[]
            {
                Field("displayName", "Pyre"),
                Field("notes",
                    "The prototype champion, and the proof that an ability is only data. Between the five " +
                    "slots: all four cast costs, a discrete count derived from a stat, a shield that " +
                    "scales from a defensive stat, a channel that pays off with commitment, and a dash " +
                    "that does something besides move.")
            });

            WriteArray(champion, "abilities", abilities);

            return champion;
        }

        // --- Passive ---------------------------------------------------------------------------

        /// <summary>
        /// <i>Cinders</i> — a continuous aura, striking more enemies the faster its owner moves.
        /// </summary>
        /// <remarks>
        /// The passive slot is a damage floor that needs no input and no cooldown (§5.5.3), and this is
        /// also where the step function lives: <b>how many</b> enemies burn is a discrete quantity derived
        /// from move speed, so it moves in whole numbers at thresholds the HUD can show (§5.6.5). The
        /// radius it burns them inside never scales.
        ///
        /// <para>Authored as damage <i>per second</i>, which is what makes the pulse interval a
        /// performance number rather than a balance one (§5.5.3), and undodgeable, because rolling dodge
        /// on every tick of an aura turns a defensive stat into a coin flip the player cannot read.</para>
        /// </remarks>
        static AbilityDefinition Cinders(List<ContentDefinition> created)
        {
            var reach = Step<SelectTargetsStep>("CindersReach", "effect.cinders_reach", created, new[]
            {
                Field("wanted", (int)TargetFaction.Enemies),
                Field("origin", (int)ShapeOrigin.Caster),
                Field("radius", CindersRadius),
                Field("coneDegrees", TargetQuery.FullCircleDegrees),
                Field("maxTargets.baseCount", 2),
                Field("maxTargets.stat", (int)StatId.MoveSpeed),
                Field("maxTargets.read", (int)StatRead.Total),
                Field("maxTargets.per", 40f),
                Field("maxTargets.maxCount", 6)
            });

            var burn = Step<DealDamageStep>("CindersBurn", "effect.cinders_burn", created, new[]
            {
                Field("amount.flat", 4f),
                Field("amount.stat", (int)StatId.Damage),
                Field("amount.read", (int)StatRead.Total),
                Field("amount.coefficient", 0.35f),
                Field("flags", (int)(DamageFlags.Ability | DamageFlags.DamageOverTime | DamageFlags.Undodgeable)),
                Field("perSecond", true),
                Field("channelBonusPerSecond", 0f)
            });

            var ability = Ability("Cinders", "ability.pyre_cinders", created, new[]
            {
                Field("displayName", "Cinders"),
                Field("role", (int)AbilityRole.Passive),
                Field("castCost", (int)CastCost.Free),
                Field("castTime", 0f),
                Field("maxChannelDuration", 0f),
                Field("cooldown", 0f),
                Field("charges", 1),
                Field("chargeLockout", 0f),
                Field("targeting", (int)TargetingMode.Self),
                Field("range", 0f)
            });

            WriteArray(ability, "steps", new EffectStep[] { reach, burn });

            return ability;
        }

        // --- Primary ---------------------------------------------------------------------------

        /// <summary>
        /// <i>Flame Lance</i> — a short rooted cone, and the champion's bread and butter.
        /// </summary>
        /// <remarks>
        /// The root is the whole point (§5.5.4): the primary is held down continuously, so paying a
        /// fraction of a second of standing still for every cast is what makes kiting a decision rather
        /// than a free action. It is kept well under the tuned ceiling, because in a dense horde a long
        /// root is a death sentence rather than a decision.
        /// </remarks>
        static AbilityDefinition Lance(List<ContentDefinition> created)
        {
            var cone = Step<SelectTargetsStep>("LanceCone", "effect.lance_cone", created, new[]
            {
                Field("wanted", (int)TargetFaction.Enemies),
                Field("origin", (int)ShapeOrigin.Caster),
                Field("radius", LanceRange),
                Field("coneDegrees", LanceCone),
                Field("maxTargets.baseCount", 0),
                Field("maxTargets.per", 0f),
                Field("maxTargets.maxCount", 0)
            });

            var strike = Step<DealDamageStep>("LanceStrike", "effect.lance_strike", created, new[]
            {
                Field("amount.flat", 20f),
                Field("amount.stat", (int)StatId.Damage),
                Field("amount.read", (int)StatRead.Total),
                Field("amount.coefficient", 0.9f),
                Field("flags", (int)DamageFlags.Ability),
                Field("perSecond", false)
            });

            var ability = Ability("FlameLance", "ability.pyre_lance", created, new[]
            {
                Field("displayName", "Flame Lance"),
                Field("role", (int)AbilityRole.Primary),
                Field("castCost", (int)CastCost.Rooted),
                Field("castTime", 0.25f),
                Field("maxChannelDuration", 0f),
                Field("cooldown", 1.1f),
                Field("charges", 1),
                Field("chargeLockout", 0f),
                Field("targeting", (int)TargetingMode.AimDirection),
                Field("range", LanceRange)
            });

            WriteArray(ability, "steps", new EffectStep[] { cone, strike });

            return ability;
        }

        // --- Secondary -------------------------------------------------------------------------

        /// <summary>
        /// <i>Ember Ward</i> — a self-shield that scales from damage reduction, and a slow around it.
        /// </summary>
        /// <remarks>
        /// The demonstration that every stat is relevant to someone (§5.6.5): damage reduction is this
        /// champion's <i>offensive</i> scaling stat, bought for the shield, with no special case anywhere
        /// in the stat layer. Free-cast, so the defensive option does not cost the repositioning that
        /// would have been the other way to survive.
        ///
        /// <para>The slow is a negative move speed modifier and nothing more. A bespoke status system
        /// would mean two places a number can come from.</para>
        /// </remarks>
        static AbilityDefinition Ward(List<ContentDefinition> created)
        {
            var barrier = Step<ApplyShieldStep>("WardBarrier", "effect.ward_barrier", created, new[]
            {
                Field("onCaster", true),
                Field("amount.flat", 25f),
                Field("amount.stat", (int)StatId.DamageReduction),
                Field("amount.read", (int)StatRead.Total),
                Field("amount.coefficient", 1.2f),
                Field("duration", 4f)
            });

            var reach = Step<SelectTargetsStep>("WardReach", "effect.ward_reach", created, new[]
            {
                Field("wanted", (int)TargetFaction.Enemies),
                Field("origin", (int)ShapeOrigin.Caster),
                Field("radius", WardRadius),
                Field("coneDegrees", TargetQuery.FullCircleDegrees),
                Field("maxTargets.baseCount", 0),
                Field("maxTargets.per", 0f)
            });

            var chill = Step<ApplyStatModifierStep>("WardChill", "effect.ward_chill", created, new[]
            {
                Field("onCaster", false),
                Field("stat", (int)StatId.MoveSpeed),
                Field("operation", (int)ModifierOperation.FlatAdd),
                Field("magnitude.flat", -45f),
                Field("magnitude.coefficient", 0f),
                Field("duration", 2.5f)
            });

            var ability = Ability("EmberWard", "ability.pyre_ward", created, new[]
            {
                Field("displayName", "Ember Ward"),
                Field("role", (int)AbilityRole.Secondary),
                Field("castCost", (int)CastCost.Free),
                Field("castTime", 0f),
                Field("maxChannelDuration", 0f),
                Field("cooldown", 11f),
                Field("charges", 1),
                Field("chargeLockout", 0f),
                Field("targeting", (int)TargetingMode.Self),
                Field("range", 0f)
            });

            WriteArray(ability, "steps", new EffectStep[] { barrier, reach, chill });

            return ability;
        }

        // --- Mobility --------------------------------------------------------------------------

        /// <summary>
        /// <i>Searing Dash</i> — two charges with a lockout, and a burst of fire where it launches from.
        /// </summary>
        /// <remarks>
        /// Mobility that also does something, which §5.5.3 insists on: a slot that only repositions is
        /// dead by the endgame, when the swarm is dense enough that there is nowhere to dash to. Two
        /// charges on their own timers with a lockout between them is the mobility-as-a-resource model
        /// from the same section, and free-cast because a mobility ability that rooted could not escape.
        ///
        /// <para><b>The damage lands where the dash begins, not along its path.</b> Path damage needs a
        /// step that follows a moving entity, which needs the projectile work in Phase 2 step 7 — so the
        /// ability is authored honestly as a burst on departure rather than pretending otherwise.</para>
        /// </remarks>
        static AbilityDefinition Dash(List<ContentDefinition> created)
        {
            var move = Step<DashStep>("DashMove", "effect.dash_move", created, new[]
            {
                Field("towardAim", false)
            });

            var reach = Step<SelectTargetsStep>("DashReach", "effect.dash_reach", created, new[]
            {
                Field("wanted", (int)TargetFaction.Enemies),
                Field("origin", (int)ShapeOrigin.Caster),
                Field("radius", DashRadius),
                Field("coneDegrees", TargetQuery.FullCircleDegrees),
                Field("maxTargets.baseCount", 0),
                Field("maxTargets.per", 0f)
            });

            var sear = Step<DealDamageStep>("DashSear", "effect.dash_sear", created, new[]
            {
                Field("amount.flat", 15f),
                Field("amount.stat", (int)StatId.Damage),
                Field("amount.read", (int)StatRead.Total),
                Field("amount.coefficient", 0.5f),
                Field("flags", (int)DamageFlags.Ability),
                Field("perSecond", false)
            });

            var ability = Ability("SearingDash", "ability.pyre_dash", created, new[]
            {
                Field("displayName", "Searing Dash"),
                Field("role", (int)AbilityRole.Mobility),
                Field("castCost", (int)CastCost.Free),
                Field("castTime", 0f),
                Field("maxChannelDuration", 0f),
                Field("cooldown", 6f),
                Field("charges", 2),
                Field("chargeLockout", 0.2f),
                Field("targeting", (int)TargetingMode.AimDirection),
                Field("range", 0f)
            });

            // Dash first: the damage is centred on where the champion is, and the order says so.
            WriteArray(ability, "steps", new EffectStep[] { move, reach, sear });

            return ability;
        }

        // --- Ultimate --------------------------------------------------------------------------

        /// <summary>
        /// <i>Crucible</i> — a channel that fortifies while it is held and detonates for what it was worth.
        /// </summary>
        /// <remarks>
        /// The §5.5.4 shape: the player chooses how much to commit, in the moment, under pressure, and the
        /// incoming damage that makes standing still lethal is also what the damage reduction is for — so
        /// the horde closing in is what makes holding longer the better decision rather than the worse one.
        ///
        /// <para>The guard is a <b>start step</b>, which is what that list exists for: it has to be true
        /// <i>during</i> the channel rather than after it. Its duration deliberately outlasts the longest
        /// possible channel, so letting go early does not end the protection early.</para>
        /// </remarks>
        static AbilityDefinition Crucible(List<ContentDefinition> created)
        {
            var guard = Step<ApplyStatModifierStep>("CrucibleGuard", "effect.crucible_guard", created, new[]
            {
                Field("onCaster", true),
                Field("stat", (int)StatId.DamageReduction),
                Field("operation", (int)ModifierOperation.FlatAdd),
                Field("magnitude.flat", 120f),
                Field("magnitude.coefficient", 0f),
                Field("duration", 3.5f)
            });

            var reach = Step<SelectTargetsStep>("CrucibleReach", "effect.crucible_reach", created, new[]
            {
                Field("wanted", (int)TargetFaction.Enemies),
                Field("origin", (int)ShapeOrigin.Caster),
                Field("radius", CrucibleRadius),
                Field("coneDegrees", TargetQuery.FullCircleDegrees),
                Field("maxTargets.baseCount", 0),
                Field("maxTargets.per", 0f)
            });

            var blast = Step<DealDamageStep>("CrucibleBlast", "effect.crucible_blast", created, new[]
            {
                Field("amount.flat", 60f),
                Field("amount.stat", (int)StatId.Damage),
                Field("amount.read", (int)StatRead.Total),
                Field("amount.coefficient", 1.5f),
                Field("flags", (int)DamageFlags.Ability),
                Field("perSecond", false),
                Field("channelBonusPerSecond", 0.6f)
            });

            var ability = Ability("Crucible", "ability.pyre_crucible", created, new[]
            {
                Field("displayName", "Crucible"),
                Field("role", (int)AbilityRole.Ultimate),
                Field("castCost", (int)CastCost.Channelled),
                Field("castTime", 0f),
                Field("maxChannelDuration", 3f),
                Field("cooldown", 40f),
                Field("charges", 1),
                Field("chargeLockout", 0f),
                Field("targeting", (int)TargetingMode.Self),
                Field("range", 0f)
            });

            WriteArray(ability, "startSteps", new EffectStep[] { guard });
            WriteArray(ability, "steps", new EffectStep[] { reach, blast });

            return ability;
        }

        // --- Asset plumbing ---------------------------------------------------------------------

        readonly struct Assignment
        {
            public Assignment(string path, object value)
            {
                Path = path;
                Value = value;
            }

            public string Path { get; }

            public object Value { get; }
        }

        static Assignment Field(string path, object value) => new Assignment(path, value);

        static AbilityDefinition Ability(
            string file,
            string id,
            List<ContentDefinition> created,
            Assignment[] fields)
        {
            var ability = Asset<AbilityDefinition>(AbilityFolder, file, id, created);

            Write(ability, fields);

            return ability;
        }

        static T Step<T>(string file, string id, List<ContentDefinition> created, Assignment[] fields)
            where T : EffectStep
        {
            var step = Asset<T>(StepFolder, file, id, created);

            Write(step, fields);

            return step;
        }

        /// <summary>
        /// Loads the asset at this path or creates it, and sets its content id.
        /// </summary>
        /// <remarks>
        /// Reused rather than deleted and remade, so the GUID survives: every reference to one of these —
        /// from an ability, from the champion, from the content library, from the prefab — is a GUID, and
        /// deleting the asset would break all of them on every rebuild.
        /// </remarks>
        static T Asset<T>(string folder, string file, string id, List<ContentDefinition> created)
            where T : ContentDefinition
        {
            var path = $"{folder}/{file}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);

            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(asset, path);
            }

            Write(asset, new[] { Field("id", id) });

            created.Add(asset);

            return asset;
        }

        /// <summary>
        /// Assigns private serialized fields by path. This is the inspector, in code.
        /// </summary>
        /// <remarks>
        /// Through <see cref="SerializedObject"/> rather than reflection, because the paths reach inside
        /// serialized structs — <c>amount.coefficient</c> is how an ability's scaling is authored — and
        /// because this is the same write path the inspector uses, so anything this produces is something
        /// a person could have produced by hand.
        /// </remarks>
        static void Write(Object target, Assignment[] fields)
        {
            var serialized = new SerializedObject(target);

            foreach (var assignment in fields)
            {
                var property = serialized.FindProperty(assignment.Path);

                if (property == null)
                {
                    Debug.LogError($"{target.name} has no serialized field '{assignment.Path}'.", target);

                    continue;
                }

                Assign(property, assignment.Value, target);
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void Assign(SerializedProperty property, object value, Object target)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Float:
                    property.floatValue = (float)value;
                    break;

                case SerializedPropertyType.Integer:
                    property.intValue = (int)value;
                    break;

                // Set by value, not by index: the enums here are contiguous from zero, and writing the
                // underlying value is what survives a reordered declaration.
                case SerializedPropertyType.Enum:
                    property.intValue = (int)value;
                    break;

                case SerializedPropertyType.Boolean:
                    property.boolValue = (bool)value;
                    break;

                case SerializedPropertyType.String:
                    property.stringValue = (string)value;
                    break;

                case SerializedPropertyType.ObjectReference:
                    property.objectReferenceValue = (Object)value;
                    break;

                default:
                    Debug.LogError(
                        $"{target.name}: no rule for writing a {property.propertyType} at '{property.propertyPath}'.",
                        target);
                    break;
            }
        }

        static void WriteArray(Object target, string field, Object[] values)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(field);

            if (property == null)
            {
                Debug.LogError($"{target.name} has no serialized field '{field}'.", target);

                return;
            }

            property.arraySize = values.Length;

            for (var i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Puts everything in the content library, which is what makes it load at runtime.
        /// </summary>
        /// <remarks>
        /// Appended rather than replaced: the library is the one asset here that a person is expected to
        /// edit by hand, and a rebuild of one champion must not drop another one.
        /// </remarks>
        static void Register(List<ContentDefinition> created)
        {
            var library = FindLibrary();

            if (library == null)
            {
                Debug.LogError(
                    "No ContentLibrary asset found, so the champion will not load. Create one via " +
                    "Assets > Create > Hype Swarm > Content Library.");

                return;
            }

            var serialized = new SerializedObject(library);
            var property = serialized.FindProperty("definitions");
            var known = new HashSet<Object>();

            for (var i = 0; i < property.arraySize; i++)
            {
                known.Add(property.GetArrayElementAtIndex(i).objectReferenceValue);
            }

            foreach (var definition in created)
            {
                if (!known.Add(definition))
                {
                    continue;
                }

                property.arraySize++;
                property.GetArrayElementAtIndex(property.arraySize - 1).objectReferenceValue = definition;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(library);
        }

        static ContentLibrary FindLibrary()
        {
            var guids = AssetDatabase.FindAssets($"t:{nameof(ContentLibrary)}");

            return guids.Length == 0
                ? null
                : AssetDatabase.LoadAssetAtPath<ContentLibrary>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            var parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');

            if (!string.IsNullOrEmpty(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
