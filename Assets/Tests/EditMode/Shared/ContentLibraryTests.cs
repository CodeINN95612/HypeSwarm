using System;
using HypeSwarm.Shared.Content;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace HypeSwarm.Shared.Tests
{
    /// <summary>
    /// The Phase 1 exit criterion, automated: a placeholder content id authored on a real
    /// ScriptableObject resolves through the registry.
    /// </summary>
    [TestFixture]
    public sealed class ContentLibraryTests
    {
        readonly System.Collections.Generic.List<ScriptableObject> created =
            new System.Collections.Generic.List<ScriptableObject>();

        [TearDown]
        public void DestroyCreatedAssets()
        {
            foreach (var asset in created)
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }

            created.Clear();
        }

        T Create<T>(Action<SerializedObject> configure = null) where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            created.Add(asset);

            if (configure != null)
            {
                var serialized = new SerializedObject(asset);
                configure(serialized);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            return asset;
        }

        PlaceholderDefinition Placeholder(string id)
        {
            return Create<PlaceholderDefinition>(serialized => serialized.FindProperty("id").stringValue = id);
        }

        ContentLibrary Library(params ContentDefinition[] definitions)
        {
            return Create<ContentLibrary>(serialized =>
            {
                var list = serialized.FindProperty("definitions");
                list.arraySize = definitions.Length;

                for (var i = 0; i < definitions.Length; i++)
                {
                    list.GetArrayElementAtIndex(i).objectReferenceValue = definitions[i];
                }
            });
        }

        [Test]
        public void PlaceholderId_ResolvesThroughTheRegistry()
        {
            var placeholder = Placeholder("placeholder.hello");
            var registry = Library(placeholder).BuildRegistry();

            var id = ContentId.Parse("placeholder.hello");

            Assert.That(registry.Contains(id), Is.True);
            Assert.That(registry.Get<PlaceholderDefinition>(id), Is.SameAs(placeholder));
            Assert.That(registry.FromIndex(registry.IndexOf(id)), Is.EqualTo(id));
            Assert.That(registry.IsSealed, Is.True, "a library builds a session-ready registry");
        }

        [Test]
        public void ContentDefinition_ParsesItsSerialisedId()
        {
            var placeholder = Placeholder("augment.shield_amp");

            Assert.That(placeholder.Id.Value, Is.EqualTo("augment.shield_amp"));
            Assert.That(placeholder.Id.Category, Is.EqualTo("augment"));
            Assert.That(placeholder.TryValidateId(out _), Is.True);
        }

        [Test]
        public void ContentDefinition_ReportsAMalformedIdInsteadOfThrowing()
        {
            var placeholder = Placeholder("Not An Id");

            Assert.That(placeholder.Id.IsValid, Is.False);
            Assert.That(placeholder.TryValidateId(out var error), Is.False);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void BuildRegistry_NamesTheAssetWhenAnIdIsMalformed()
        {
            var broken = Placeholder("Not An Id");
            broken.name = "BrokenPlaceholder";

            var exception = Assert.Throws<InvalidOperationException>(() => Library(broken).BuildRegistry());

            Assert.That(exception.Message, Does.Contain("BrokenPlaceholder"),
                "the author needs to know which asset to open");
        }

        [Test]
        public void BuildRegistry_RejectsAnEmptySlot()
        {
            var library = Create<ContentLibrary>(serialized =>
            {
                var list = serialized.FindProperty("definitions");
                list.arraySize = 1;
                list.GetArrayElementAtIndex(0).objectReferenceValue = null;
            });

            var exception = Assert.Throws<InvalidOperationException>(() => library.BuildRegistry());

            Assert.That(exception.Message, Does.Contain("0"), "the empty slot's index is what makes it findable");
        }

        [Test]
        public void BuildRegistry_RejectsDuplicateIds()
        {
            var library = Library(Placeholder("placeholder.same"), Placeholder("placeholder.same"));

            Assert.Throws<ArgumentException>(() => library.BuildRegistry());
        }

        [Test]
        public void BuildRegistry_CanLeaveTheRegistryUnsealedForIncrementalLoading()
        {
            var registry = Library(Placeholder("placeholder.hello")).BuildRegistry(seal: false);

            Assert.That(registry.IsSealed, Is.False);
            Assert.That(registry.Count, Is.EqualTo(1));
        }

        [Test]
        public void EmptyLibrary_BuildsAnEmptySealedRegistry()
        {
            var registry = Library().BuildRegistry();

            Assert.That(registry.Count, Is.Zero);
            Assert.That(registry.IsSealed, Is.True);
            Assert.That(registry.OrderedIds, Is.Empty);
        }
    }
}
