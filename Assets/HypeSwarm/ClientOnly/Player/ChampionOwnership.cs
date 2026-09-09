using HypeSwarm.Shared.Net;
using Mirror;
using UnityEngine;

namespace HypeSwarm.ClientOnly.Player
{
    /// <summary>
    /// Turns one of the five spawned champions into <i>this</i> player's champion: it takes input,
    /// it owns the camera, and it is the only one with a reticle.
    /// </summary>
    /// <remarks>
    /// The alternative — an <c>isLocalPlayer</c> check inside every component that should not run on
    /// a copy — spreads the same condition across the codebase and fails quietly when the next
    /// component forgets it. One component that switches the others on is a single place to read and
    /// a single place to get wrong.
    ///
    /// <para>The controlled components ship <b>disabled on the prefab</b>. Off-by-default means a
    /// remote champion never reads the keyboard even for the frame between spawning and authority
    /// arriving; the opposite default fails in the direction where four other players drive your
    /// character.</para>
    /// </remarks>
    [RequireComponent(typeof(ChampionNetworkState))]
    [DisallowMultipleComponent]
    public sealed class ChampionOwnership : NetworkBehaviour
    {
        [SerializeField]
        [Tooltip("Input-driven controller. Must be disabled on the prefab; enabled here for the owner.")]
        ChampionController controller;

        [SerializeField]
        [Tooltip("Objects only the owning player should see — the reticle and anything like it.")]
        GameObject[] localOnly = new GameObject[0];

        void Awake()
        {
            if (controller == null)
            {
                controller = GetComponent<ChampionController>();
            }

            SetLocalOnlyVisible(false);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            // A champion that is not ours keeps its transform from the network and nothing else.
            if (!isOwned)
            {
                name = $"{name} (remote)";
            }
        }

        public override void OnStartAuthority()
        {
            base.OnStartAuthority();

            if (controller != null)
            {
                controller.enabled = true;
            }

            SetLocalOnlyVisible(true);
            TakeCamera();
        }

        public override void OnStopAuthority()
        {
            base.OnStopAuthority();

            if (controller != null)
            {
                controller.enabled = false;
            }

            SetLocalOnlyVisible(false);
        }

        /// <summary>
        /// Points the scene's camera rig at this champion and snaps it there. Snapping matters: the
        /// rig starts at the origin, and smoothing in from across the arena is a second of the
        /// player watching the floor go past on every spawn.
        /// </summary>
        void TakeCamera()
        {
            var rig = FindAnyObjectByType<ChampionCameraRig>();

            if (rig == null)
            {
                Debug.LogWarning(
                    "No ChampionCameraRig in the scene, so the camera will not follow this champion.",
                    this);
                return;
            }

            rig.Follow(controller);

            if (controller != null)
            {
                controller.AssignCamera(rig.GetComponent<Camera>());
            }
        }

        void SetLocalOnlyVisible(bool visible)
        {
            foreach (var target in localOnly)
            {
                if (target != null)
                {
                    target.SetActive(visible);
                }
            }
        }
    }
}
