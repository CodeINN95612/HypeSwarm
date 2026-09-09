using System.Collections.Generic;
using System.IO;
using System.Linq;
using HypeSwarm.Shared.Content;
using HypeSwarm.Shared.Tuning;
using UnityEditor;
using UnityEngine;

namespace HypeSwarm.Editor
{
    /// <summary>
    /// One-click check that the authored content and tuning files are actually loadable.
    /// </summary>
    /// <remarks>
    /// Both systems fail quietly by design — the registry only throws when something tries to build
    /// it, and a missing tuning key falls back to a hardcoded default rather than stopping the
    /// game. That is right at runtime and useless while authoring, where a typo can sit unnoticed
    /// for weeks. This window is where those mistakes become visible.
    ///
    /// <para>The judging lives in <see cref="ContentValidation"/> and is unit tested. This class
    /// only finds the assets and draws the result.</para>
    /// </remarks>
    public sealed class ValidationWindow : EditorWindow
    {
        readonly List<ValidationIssue> contentIssues = new List<ValidationIssue>();
        readonly List<ValidationIssue> tuningIssues = new List<ValidationIssue>();

        ContentLibrary library;
        ContentRegistry registry;
        TuningConfig tuning;
        string[] tuningDirectories;
        Vector2 scroll;
        bool showContentTable;
        bool showTuningTable;
        bool hasRun;

        [MenuItem("Hype Swarm/Validate Content and Tuning %#v")]
        public static void Open()
        {
            var window = GetWindow<ValidationWindow>();
            window.titleContent = new GUIContent("Hype Swarm Validation");
            window.minSize = new Vector2(460f, 320f);
            window.Validate();
            window.Show();
        }

        void OnEnable()
        {
            if (!hasRun)
            {
                Validate();
            }
        }

        void Validate()
        {
            hasRun = true;

            library = FindSingleLibrary(out var libraryIssue);

            var allDefinitions = LoadAll<ContentDefinition>();

            contentIssues.Clear();

            if (libraryIssue.HasValue)
            {
                contentIssues.Add(libraryIssue.Value);
            }

            contentIssues.AddRange(ContentValidation.ValidateContent(library, allDefinitions));

            registry = null;

            if (library != null && contentIssues.All(issue => issue.Severity != ValidationSeverity.Error))
            {
                registry = library.BuildRegistry();
            }

            tuningDirectories = TuningLoader.DefaultDirectories().ToArray();
            tuning = TuningLoader.Load(tuningDirectories, out var loadErrors);

            // Read every key once so malformed values are discovered rather than waiting for some
            // future caller to trip over them.
            foreach (var key in tuning.AllKeys)
            {
                tuning.TryGetRaw(key, out _, out _);
            }

            tuningIssues.Clear();
            tuningIssues.AddRange(ContentValidation.ValidateTuning(tuning, loadErrors));

            Repaint();
        }

        static ContentLibrary FindSingleLibrary(out ValidationIssue? issue)
        {
            var libraries = LoadAll<ContentLibrary>();
            issue = null;

            if (libraries.Count > 1)
            {
                issue = new ValidationIssue(
                    ValidationSeverity.Warning,
                    $"{libraries.Count} ContentLibrary assets exist; validating '{libraries[0].name}'. " +
                    "Only one can define the network index table — delete or merge the others.",
                    libraries[0]);
            }

            return libraries.Count > 0 ? libraries[0] : null;
        }

        static List<T> LoadAll<T>() where T : Object
        {
            return AssetDatabase.FindAssets($"t:{typeof(T).Name}")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<T>)
                .Where(asset => asset != null)
                .OrderBy(asset => asset.name, System.StringComparer.Ordinal)
                .ToList();
        }

        void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Revalidate", EditorStyles.toolbarButton, GUILayout.Width(90f)))
                {
                    Validate();
                }

                GUILayout.FlexibleSpace();
                GUILayout.Label(Verdict(), EditorStyles.miniLabel);
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);

            DrawContent();
            EditorGUILayout.Space();
            DrawTuning();

            EditorGUILayout.EndScrollView();
        }

        string Verdict()
        {
            var errors = contentIssues.Concat(tuningIssues).Count(i => i.Severity == ValidationSeverity.Error);
            var warnings = contentIssues.Concat(tuningIssues).Count(i => i.Severity == ValidationSeverity.Warning);

            if (errors > 0)
            {
                return $"{errors} error{(errors == 1 ? "" : "s")}, {warnings} warning{(warnings == 1 ? "" : "s")}";
            }

            return warnings > 0 ? $"{warnings} warning{(warnings == 1 ? "" : "s")}" : "all clear";
        }

        void DrawContent()
        {
            EditorGUILayout.LabelField("Content", EditorStyles.boldLabel);

            using (new EditorGUI.IndentLevelScope())
            {
                if (registry != null)
                {
                    EditorGUILayout.LabelField(
                        $"{registry.Count} registered · manifest 0x{registry.ManifestHash:X8}",
                        EditorStyles.miniLabel);
                }

                DrawIssues(contentIssues);

                if (registry == null || registry.Count == 0)
                {
                    return;
                }

                showContentTable = EditorGUILayout.Foldout(showContentTable, "Registered ids", true);

                if (!showContentTable)
                {
                    return;
                }

                using (new EditorGUI.IndentLevelScope())
                {
                    foreach (var id in registry.OrderedIds)
                    {
                        var definition = registry.Get<ContentDefinition>(id);

                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.LabelField($"[{registry.IndexOf(id)}]  {id}", GUILayout.MinWidth(220f));

                            if (GUILayout.Button(definition.name, EditorStyles.miniButton, GUILayout.Width(160f)))
                            {
                                Selection.activeObject = definition;
                                EditorGUIUtility.PingObject(definition);
                            }
                        }
                    }
                }
            }
        }

        void DrawTuning()
        {
            EditorGUILayout.LabelField("Tuning", EditorStyles.boldLabel);

            using (new EditorGUI.IndentLevelScope())
            {
                foreach (var directory in tuningDirectories)
                {
                    var exists = Directory.Exists(directory);
                    EditorGUILayout.LabelField($"{(exists ? "•" : "—")} {directory}", EditorStyles.miniLabel);
                }

                if (tuning != null)
                {
                    EditorGUILayout.LabelField(
                        $"{tuning.Layers.Count} layer{(tuning.Layers.Count == 1 ? "" : "s")} · {tuning.AllKeys.Count} keys",
                        EditorStyles.miniLabel);
                }

                DrawIssues(tuningIssues);

                if (tuning == null || tuning.AllKeys.Count == 0)
                {
                    return;
                }

                showTuningTable = EditorGUILayout.Foldout(showTuningTable, "Effective values", true);

                if (!showTuningTable)
                {
                    return;
                }

                using (new EditorGUI.IndentLevelScope())
                {
                    foreach (var key in tuning.AllKeys.OrderBy(k => k, System.StringComparer.Ordinal))
                    {
                        tuning.TryGetRaw(key, out var value, out var source);
                        EditorGUILayout.LabelField($"{key} = {value}", $"from {source}", EditorStyles.miniLabel);
                    }
                }
            }
        }

        static void DrawIssues(List<ValidationIssue> issues)
        {
            if (issues.Count == 0)
            {
                EditorGUILayout.HelpBox("No problems found.", MessageType.Info);
                return;
            }

            foreach (var issue in issues.OrderByDescending(i => i.Severity))
            {
                EditorGUILayout.HelpBox(issue.Message, ToMessageType(issue.Severity));

                if (issue.Context == null)
                {
                    continue;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button($"Select {issue.Context.name}", EditorStyles.miniButton, GUILayout.Width(200f)))
                    {
                        Selection.activeObject = issue.Context;
                        EditorGUIUtility.PingObject(issue.Context);
                    }
                }
            }
        }

        static MessageType ToMessageType(ValidationSeverity severity)
        {
            switch (severity)
            {
                case ValidationSeverity.Error:
                    return MessageType.Error;
                case ValidationSeverity.Warning:
                    return MessageType.Warning;
                default:
                    return MessageType.Info;
            }
        }
    }
}
