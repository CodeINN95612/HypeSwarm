using System.Linq;
using HypeSwarm.ClientOnly.Controls;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.InputSystem;

namespace HypeSwarm.ClientOnly.Tests
{
    /// <summary>
    /// Guards the seam between <see cref="GameplayInput"/> and the asset it reads.
    /// </summary>
    /// <remarks>
    /// Actions are found by name, which the input system resolves at runtime and silently returns
    /// nothing for when the name is wrong. The failure that produces is a character who does not
    /// move, with no error and nothing in the console to explain it — worth a few seconds of test
    /// time to turn into a red line naming the missing action.
    ///
    /// <para>This deliberately checks that bindings <i>exist</i>, not what they are. Which key
    /// dashes is a preference, and rebinding is coming (Phase 5); an unbound action is a bug in any
    /// binding scheme.</para>
    /// </remarks>
    [TestFixture]
    public sealed class GameplayInputAssetTests
    {
        const string AssetPath = "Assets/HypeSwarm/ClientOnly/Controls/HypeSwarmControls.inputactions";

        InputActionAsset asset;

        [SetUp]
        public void LoadAsset()
        {
            asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetPath);

            Assert.That(asset, Is.Not.Null, $"no input actions asset at {AssetPath}");
        }

        [Test]
        public void Asset_ContainsTheGameplayMap()
        {
            Assert.That(asset.FindActionMap(GameplayInput.MapName), Is.Not.Null);
        }

        [Test]
        public void Asset_ContainsEveryActionTheReaderRequires()
        {
            var map = asset.FindActionMap(GameplayInput.MapName, throwIfNotFound: true);
            var present = map.actions.Select(action => action.name).ToList();

            Assert.That(present, Is.SupersetOf(GameplayInput.RequiredActions));
        }

        [Test]
        public void EveryRequiredAction_HasAtLeastOneBinding()
        {
            var map = asset.FindActionMap(GameplayInput.MapName, throwIfNotFound: true);

            foreach (var name in GameplayInput.RequiredActions)
            {
                var action = map.FindAction(name, throwIfNotFound: true);

                Assert.That(
                    action.bindings.Count,
                    Is.GreaterThan(0),
                    $"'{name}' exists but is bound to nothing, so it can never fire");
            }
        }

        [Test]
        public void MoveAndAim_AreVector2Actions()
        {
            var map = asset.FindActionMap(GameplayInput.MapName, throwIfNotFound: true);

            foreach (var name in new[] { GameplayInput.MoveActionName, GameplayInput.AimActionName })
            {
                Assert.That(
                    map.FindAction(name, throwIfNotFound: true).expectedControlType,
                    Is.EqualTo("Vector2"),
                    $"'{name}' is read with ReadValue<Vector2>, which returns zero for any other type");
            }
        }

        /// <summary>
        /// Move is a value action with an initial state check so that a key already held when the
        /// map enables is seen. Without it, holding a direction through a scene load leaves the
        /// champion standing still until the key is released and pressed again.
        /// </summary>
        [Test]
        public void Move_IsAValueActionThatChecksItsInitialState()
        {
            var move = asset
                .FindActionMap(GameplayInput.MapName, throwIfNotFound: true)
                .FindAction(GameplayInput.MoveActionName, throwIfNotFound: true);

            Assert.That(move.type, Is.EqualTo(InputActionType.Value));
            Assert.That(move.wantsInitialStateCheck, Is.True);
        }

        [Test]
        public void Reader_ResolvesEveryActionAgainstTheShippedAsset()
        {
            Assert.DoesNotThrow(
                () =>
                {
                    using (new GameplayInput(asset))
                    {
                    }
                },
                "constructing the reader is the same lookup the game performs at spawn");
        }
    }
}
