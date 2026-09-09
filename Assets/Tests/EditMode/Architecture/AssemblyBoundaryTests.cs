using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HypeSwarm.ClientOnly;
using HypeSwarm.Shared;
using HypeSwarm.ServerOnly;
using NUnit.Framework;
using UnityEngine;

namespace HypeSwarm.Architecture.Tests
{
    /// <summary>
    /// Enforces the three-way assembly split (spec §13.1) and the presentation separation it exists
    /// to protect (§12).
    /// </summary>
    /// <remarks>
    /// The spec's claim is that the asmdef boundary is "the only enforcement that holds". That is
    /// nearly true — but only for whole assemblies like <c>UnityEngine.UI</c>, which an asmdef can
    /// decline to reference. Engine <i>modules</i> such as particles and audio come along with the
    /// engine reference and cannot be excluded in the asmdef file, so nothing stops a
    /// <c>Shared</c> effect step from calling <c>AudioSource.PlayClipAtPoint</c>.
    ///
    /// <para>These tests close that gap. The C# compiler only emits an assembly reference for an
    /// assembly whose types are actually used, so the referenced-assembly list of the compiled
    /// <c>HypeSwarm.Shared</c> is a faithful record of what the code truly touches — a stronger
    /// check than reading the asmdef, and the one that fails the moment someone spawns a particle
    /// system from gameplay code.</para>
    /// </remarks>
    [TestFixture]
    public sealed class AssemblyBoundaryTests
    {
        static readonly Assembly Shared = typeof(SharedAssemblyMarker).Assembly;
        static readonly Assembly ClientOnly = typeof(ClientOnlyAssemblyMarker).Assembly;
        static readonly Assembly ServerOnly = typeof(ServerOnlyAssemblyMarker).Assembly;

        /// <summary>
        /// Presentation, input, and editor assemblies. Gameplay logic must run without any of them
        /// so the host can run headless and clients can replay their own casts before the host
        /// confirms (§5.5.1, §12).
        /// </summary>
        static readonly string[] PresentationAssemblies =
        {
            "UnityEngine.UI",
            "UnityEngine.UIElementsModule",
            "UnityEngine.IMGUIModule",
            "UnityEngine.ParticleSystemModule",
            "UnityEngine.AudioModule",
            "UnityEngine.VideoModule",
            "UnityEngine.AnimationModule",
            "Unity.TextMeshPro",
            "Unity.InputSystem",
            "UnityEditor",
            "UnityEditor.CoreModule"
        };

        static IEnumerable<string> ReferencedAssemblies(Assembly assembly)
        {
            return assembly.GetReferencedAssemblies().Select(reference => reference.Name);
        }

        static void AssertDoesNotReference(Assembly assembly, IEnumerable<string> forbidden, string why)
        {
            var referenced = new HashSet<string>(ReferencedAssemblies(assembly), StringComparer.Ordinal);
            var violations = forbidden.Where(referenced.Contains).ToArray();

            Assert.That(
                violations,
                Is.Empty,
                $"{assembly.GetName().Name} references {string.Join(", ", violations)}. {why}");
        }

        // --- Shared -----------------------------------------------------------------------

        [Test]
        public void Shared_DoesNotReferencePresentationOrInput()
        {
            AssertDoesNotReference(
                Shared,
                PresentationAssemblies,
                "Gameplay logic must run headless. Effect steps emit events; move the visual, the " +
                "sound, or the input read into HypeSwarm.ClientOnly and subscribe there (spec §12).");
        }

        [Test]
        public void Shared_DoesNotReferenceClientOrServer()
        {
            AssertDoesNotReference(
                Shared,
                new[] { "HypeSwarm.ClientOnly", "HypeSwarm.ServerOnly" },
                "Shared is the base of the dependency graph. Both sides depend on it; it depends on neither.");
        }

        // --- ClientOnly / ServerOnly -------------------------------------------------------

        [Test]
        public void ClientOnly_DoesNotReferenceServerOnly()
        {
            AssertDoesNotReference(
                ClientOnly,
                new[] { "HypeSwarm.ServerOnly" },
                "A client must not be able to run host-authoritative code. Shared logic belongs in HypeSwarm.Shared.");
        }

        [Test]
        public void ServerOnly_DoesNotReferenceClientOnly()
        {
            AssertDoesNotReference(
                ServerOnly,
                new[] { "HypeSwarm.ClientOnly" },
                "The dedicated server build has no presentation layer to reference (spec §23).");
        }

        [Test]
        public void ServerOnly_DoesNotReferencePresentationOrInput()
        {
            AssertDoesNotReference(
                ServerOnly,
                PresentationAssemblies,
                "Host authority runs in a headless build with no window, no audio device, and no input.");
        }

        // --- The asmdef files themselves ---------------------------------------------------

        [Serializable]
        class AssemblyDefinitionFile
        {
#pragma warning disable CS0649 // assigned by JsonUtility
            public string name;
            public string[] references;
            public string[] includePlatforms;
#pragma warning restore CS0649
        }

        static IEnumerable<(string Path, AssemblyDefinitionFile Definition)> ProjectAsmdefs()
        {
            var root = Path.Combine(Application.dataPath, "HypeSwarm");

            foreach (var path in Directory.GetFiles(root, "*.asmdef", SearchOption.AllDirectories))
            {
                var definition = JsonUtility.FromJson<AssemblyDefinitionFile>(File.ReadAllText(path));

                if (definition != null)
                {
                    yield return (path, definition);
                }
            }
        }

        static readonly string[] RuntimeAssemblyNames =
        {
            "HypeSwarm.Shared",
            "HypeSwarm.ClientOnly",
            "HypeSwarm.ServerOnly"
        };

        static bool IsEditorOnly(AssemblyDefinitionFile definition)
        {
            var platforms = definition.includePlatforms ?? Array.Empty<string>();
            return platforms.Length == 1 && platforms[0] == "Editor";
        }

        /// <summary>
        /// Pins the whole set. A fourth runtime assembly is a real architectural decision, and it
        /// should require editing this list rather than appearing by accident.
        /// </summary>
        [Test]
        public void AsmdefsUnderHypeSwarm_AreTheExpectedSet()
        {
            var names = ProjectAsmdefs().Select(entry => entry.Definition.name).ToArray();

            Assert.That(names, Is.EquivalentTo(RuntimeAssemblyNames.Concat(new[] { "HypeSwarm.Editor" })));
        }

        /// <summary>
        /// Editor-only assemblies are exempt from the reflection checks above — UnityEditor is
        /// theirs to use — so a runtime assembly quietly restricted to the Editor would slip past
        /// every other test in this fixture while also failing to ship.
        /// </summary>
        [Test]
        public void RuntimeAsmdefs_AreNotEditorOnly()
        {
            foreach (var (path, definition) in ProjectAsmdefs())
            {
                if (!RuntimeAssemblyNames.Contains(definition.name))
                {
                    continue;
                }

                Assert.That(
                    IsEditorOnly(definition),
                    Is.False,
                    $"{Path.GetFileName(path)} is restricted to the Editor and would not ship.");
            }
        }

        [Test]
        public void EditorAsmdef_IsEditorOnly()
        {
            var editor = ProjectAsmdefs().Single(entry => entry.Definition.name == "HypeSwarm.Editor");

            Assert.That(
                IsEditorOnly(editor.Definition),
                Is.True,
                "HypeSwarm.Editor must not ship in a build.");
        }

        [Test]
        public void RuntimeAssemblies_DoNotReferenceTheEditorAssembly()
        {
            foreach (var assembly in new[] { Shared, ClientOnly, ServerOnly })
            {
                AssertDoesNotReference(
                    assembly,
                    new[] { "HypeSwarm.Editor" },
                    "Editor tooling is stripped from a build; a runtime dependency on it would not compile there.");
            }
        }

        /// <summary>
        /// Catches the intent before the code: a declared reference is someone announcing they are
        /// about to cross the boundary, and it is cheaper to fail here than after the call sites exist.
        /// </summary>
        [Test]
        public void SharedAsmdef_DeclaresNoForbiddenReference()
        {
            var shared = ProjectAsmdefs().Single(entry => entry.Definition.name == "HypeSwarm.Shared");
            var declared = shared.Definition.references ?? Array.Empty<string>();

            var forbidden = PresentationAssemblies
                .Concat(new[] { "HypeSwarm.ClientOnly", "HypeSwarm.ServerOnly" })
                .ToArray();

            Assert.That(
                declared.Where(reference => forbidden.Contains(reference)),
                Is.Empty,
                "HypeSwarm.Shared.asmdef declares a reference it is not allowed to have.");
        }
    }
}
