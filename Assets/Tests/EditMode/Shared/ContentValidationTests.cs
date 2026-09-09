using System.Collections.Generic;
using System.Linq;
using HypeSwarm.Shared.Content;
using HypeSwarm.Shared.Tuning;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace HypeSwarm.Shared.Tests
{
    [TestFixture]
    public sealed class ContentValidationTests
    {
        readonly List<ScriptableObject> created = new List<ScriptableObject>();

        [TearDown]
        public void DestroyCreatedAssets()
        {
            foreach (var asset in created)
            {
                Object.DestroyImmediate(asset);
            }

            created.Clear();
        }

        PlaceholderDefinition Definition(string name, string id)
        {
            var asset = ScriptableObject.CreateInstance<PlaceholderDefinition>();
            asset.name = name;
            created.Add(asset);

            var serialized = new SerializedObject(asset);
            serialized.FindProperty("id").stringValue = id;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return asset;
        }

        ContentLibrary Library(params ContentDefinition[] definitions)
        {
            var asset = ScriptableObject.CreateInstance<ContentLibrary>();
            asset.name = "TestLibrary";
            created.Add(asset);

            var serialized = new SerializedObject(asset);
            var list = serialized.FindProperty("definitions");
            list.arraySize = definitions.Length;

            for (var i = 0; i < definitions.Length; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = definitions[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            return asset;
        }

        // List rather than array: NUnit's Has.Count constraint reflects for a Count property, which
        // an array does not have.
        static List<ValidationIssue> Errors(IReadOnlyList<ValidationIssue> issues)
        {
            return issues.Where(issue => issue.Severity == ValidationSeverity.Error).ToList();
        }

        static List<ValidationIssue> Warnings(IReadOnlyList<ValidationIssue> issues)
        {
            return issues.Where(issue => issue.Severity == ValidationSeverity.Warning).ToList();
        }

        // --- Content ---------------------------------------------------------------------

        [Test]
        public void CleanLibrary_HasNoIssues()
        {
            var a = Definition("A", "augment.a");
            var b = Definition("B", "augment.b");

            var issues = ContentValidation.ValidateContent(Library(a, b), new ContentDefinition[] { a, b });

            Assert.That(issues, Is.Empty);
        }

        [Test]
        public void MissingLibrary_IsAnError()
        {
            var issues = ContentValidation.ValidateContent(null, new ContentDefinition[0]);

            Assert.That(Errors(issues), Has.Count.EqualTo(1));
        }

        [Test]
        public void EmptySlot_IsAnError()
        {
            var library = Library(null, Definition("A", "augment.a"));

            var issues = ContentValidation.ValidateContent(library, new ContentDefinition[0]);

            Assert.That(Errors(issues), Has.Exactly(1).Matches<ValidationIssue>(i => i.Message.Contains("slot 0")));
        }

        [Test]
        public void MalformedId_IsAnErrorNamingTheAsset()
        {
            var broken = Definition("BrokenAugment", "Not An Id");

            var issues = ContentValidation.ValidateContent(Library(broken), new ContentDefinition[] { broken });

            Assert.That(Errors(issues), Has.Exactly(1).Matches<ValidationIssue>(
                i => i.Message.Contains("BrokenAugment") && ReferenceEquals(i.Context, broken)));
        }

        [Test]
        public void DuplicateId_IsAnErrorNamingBothAssets()
        {
            var first = Definition("First", "augment.same");
            var second = Definition("Second", "augment.same");

            var issues = ContentValidation.ValidateContent(
                Library(first, second),
                new ContentDefinition[] { first, second });

            Assert.That(Errors(issues), Has.Exactly(1).Matches<ValidationIssue>(
                i => i.Message.Contains("First") && i.Message.Contains("Second")));
        }

        /// <summary>
        /// The failure mode this catches is the quiet one: the asset exists, looks authored, and
        /// simply never loads. Nothing at runtime says why, because nothing at runtime knows it was
        /// meant to be there.
        /// </summary>
        [Test]
        public void DefinitionOutsideTheLibrary_IsAWarning()
        {
            var registered = Definition("Registered", "augment.a");
            var orphan = Definition("Orphan", "augment.b");

            var issues = ContentValidation.ValidateContent(
                Library(registered),
                new ContentDefinition[] { registered, orphan });

            Assert.That(Warnings(issues), Has.Exactly(1).Matches<ValidationIssue>(
                i => i.Message.Contains("Orphan") && ReferenceEquals(i.Context, orphan)));
        }

        [Test]
        public void DefinitionListedTwice_IsAWarningRatherThanADuplicateIdError()
        {
            var definition = Definition("A", "augment.a");

            var issues = ContentValidation.ValidateContent(
                Library(definition, definition),
                new ContentDefinition[] { definition });

            Assert.That(Errors(issues), Is.Empty, "the same asset twice is untidy, not broken");
            Assert.That(Warnings(issues), Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Validation exists to review work, so it must report every problem at once. Stopping at
        /// the first one turns three typos into three round trips.
        /// </summary>
        [Test]
        public void EveryProblemIsReported_NotJustTheFirst()
        {
            var brokenA = Definition("BrokenA", "NOPE");
            var brokenB = Definition("BrokenB", "also bad");
            var orphan = Definition("Orphan", "augment.c");

            var issues = ContentValidation.ValidateContent(
                Library(null, brokenA, brokenB),
                new ContentDefinition[] { brokenA, brokenB, orphan });

            Assert.That(Errors(issues), Has.Count.EqualTo(3), "empty slot plus two malformed ids");
            Assert.That(Warnings(issues), Has.Count.EqualTo(1), "the orphan");
        }

        // --- Tuning ----------------------------------------------------------------------

        [Test]
        public void TuningLoadErrors_BecomeErrors()
        {
            var config = new TuningConfig(new DictionaryTuningSource("test"));

            var issues = ContentValidation.ValidateTuning(config, new[] { "core.tuning:3: broken line" });

            Assert.That(Errors(issues), Has.Exactly(1).Matches<ValidationIssue>(i => i.Message.Contains("core.tuning:3")));
        }

        [Test]
        public void NoTuningLayers_IsAWarning()
        {
            var issues = ContentValidation.ValidateTuning(new TuningConfig(), new string[0]);

            Assert.That(Warnings(issues), Has.Count.EqualTo(1));
        }

        [Test]
        public void MalformedTuningValue_IsAnError()
        {
            var config = new TuningConfig(new DictionaryTuningSource("test").Set("a.b", "twelve"));
            config.GetFloat("a.b", 0f);

            var issues = ContentValidation.ValidateTuning(config, new string[0]);

            Assert.That(Errors(issues), Has.Exactly(1).Matches<ValidationIssue>(i => i.Message.Contains("a.b")));
        }

        [Test]
        public void MissingTuningKey_IsAWarning()
        {
            var config = new TuningConfig(new DictionaryTuningSource("test").Set("a.b", "1"));
            config.GetFloat("a.absent", 0f);

            var issues = ContentValidation.ValidateTuning(config, new string[0]);

            Assert.That(Warnings(issues), Has.Exactly(1).Matches<ValidationIssue>(i => i.Message.Contains("a.absent")));
        }

        [Test]
        public void CleanTuning_HasNoIssues()
        {
            var config = new TuningConfig(new DictionaryTuningSource("test").Set("a.b", "1.5"));
            config.GetFloat("a.b", 0f);

            Assert.That(ContentValidation.ValidateTuning(config, new string[0]), Is.Empty);
        }
    }
}
