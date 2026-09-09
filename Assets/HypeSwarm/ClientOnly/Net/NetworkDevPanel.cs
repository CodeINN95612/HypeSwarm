using System;
using System.Collections.Generic;
using HypeSwarm.ClientOnly.Steam;
using HypeSwarm.Shared.Net;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HypeSwarm.ClientOnly.Net
{
    /// <summary>
    /// Development scaffolding: host, connect, invite, and see who is in the lobby, from a corner of
    /// the screen.
    /// </summary>
    /// <remarks>
    /// This is not the game's UI and is not meant to become it — the real lobby and HUD are Phase 5
    /// (§20). It exists because the exit criterion for this step is five machines in one lobby, and
    /// verifying that needs a way to connect five builds that does not involve editing a scene on
    /// each of them.
    ///
    /// <para>IMGUI on purpose. It needs no canvas, no prefab, no fonts, and no layout work, and it
    /// will be deleted rather than restyled. Building it out of the real UI stack would make it
    /// look like something worth keeping.</para>
    /// </remarks>
    [RequireComponent(typeof(NetworkBootstrap))]
    public sealed class NetworkDevPanel : MonoBehaviour
    {
        [SerializeField]
        HypeSwarmNetworkManager manager;

        [SerializeField]
        NetworkBootstrap bootstrap;

        [SerializeField]
        [Tooltip("Key that shows and hides the panel.")]
        Key toggleKey = Key.F1;

        [SerializeField]
        bool visible = true;

        string address = "localhost";
        string port = NetworkLaunchOptions.DefaultPort.ToString();
        Vector2 scroll;
        Vector2 friendScroll;
        bool showFriends;
        IReadOnlyList<SteamFriend> friends = Array.Empty<SteamFriend>();

        void Awake()
        {
            if (bootstrap == null)
            {
                bootstrap = GetComponent<NetworkBootstrap>();
            }

            if (manager == null)
            {
                manager = GetComponent<HypeSwarmNetworkManager>();
            }
        }

        void Update()
        {
            // Read straight off the device rather than through the Gameplay action map. This panel
            // has to work when the map is disabled, which is exactly when it is needed.
            var keyboard = Keyboard.current;

            if (keyboard != null && keyboard[toggleKey].wasPressedThisFrame)
            {
                visible = !visible;
            }
        }

        void OnGUI()
        {
            if (!visible)
            {
                GUI.Label(new Rect(10f, 10f, 300f, 20f), $"{toggleKey} — network panel");
                return;
            }

            using (new GUILayout.AreaScope(new Rect(10f, 10f, 320f, 540f), GUIContent.none, GUI.skin.box))
            {
                GUILayout.Label($"<b>Hype Swarm — network ({toggleKey} hides)</b>", RichLabel());
                GUILayout.Label(bootstrap == null ? "no bootstrap" : bootstrap.Selection.ToString(), Wrapped());

                GUILayout.Space(4f);

                if (NetworkServer.active || NetworkClient.active)
                {
                    DrawConnected();
                }
                else
                {
                    DrawDisconnected();
                }
            }
        }

        void DrawDisconnected()
        {
            if (GUILayout.Button("Host (direct)"))
            {
                bootstrap.HostDirect();
            }

            GUILayout.BeginHorizontal();
            address = GUILayout.TextField(address);
            port = GUILayout.TextField(port, GUILayout.Width(60f));
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Connect"))
            {
                bootstrap.JoinDirect(
                    address,
                    ushort.TryParse(port, out var parsed) ? parsed : NetworkLaunchOptions.DefaultPort);
            }

            GUILayout.Space(6f);

            GUI.enabled = SteamLifecycle.IsRunning;

            if (GUILayout.Button("Host on Steam"))
            {
                bootstrap.HostOnSteam();
            }

            GUI.enabled = true;

            GUILayout.Label(
                SteamLifecycle.IsRunning
                    ? $"Steam: {SteamLifecycle.LocalName}"
                    : $"Steam: unavailable — {SteamLifecycle.FailureReason}",
                Wrapped());
        }

        void DrawConnected()
        {
            var role = NetworkServer.active && NetworkClient.active ? "host"
                : NetworkServer.active ? "server"
                : NetworkClient.isConnected ? "client" : "connecting";

            GUILayout.Label($"Running as <b>{role}</b>", RichLabel());

            if (NetworkClient.isConnected)
            {
                GUILayout.Label($"Round trip: {NetworkTime.rtt * 1000d:0} ms");
            }

            if (NetworkServer.active && manager != null)
            {
                var roster = manager.Roster;

                GUILayout.Label($"Lobby {roster.Count}/{roster.Capacity}");

                scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(90f));

                foreach (var member in roster.Members)
                {
                    GUILayout.Label(member.ToString());
                }

                GUILayout.EndScrollView();

                DrawInvites();
            }

            GUILayout.Space(6f);

            if (GUILayout.Button("Disconnect"))
            {
                bootstrap.Stop();
            }
        }

        /// <summary>
        /// Two ways to invite, because the good one is not always available. The overlay dialog is
        /// Steam's own and is what players will use; the friend list underneath it exists for every
        /// context the overlay is not injected into, starting with the Editor.
        /// </summary>
        void DrawInvites()
        {
            if (!SteamLifecycle.IsRunning)
            {
                return;
            }

            GUILayout.BeginHorizontal();

            GUI.enabled = SteamLobbyService.OverlayAvailable;

            if (GUILayout.Button("Invite friends…"))
            {
                bootstrap.InviteFriends();
            }

            GUI.enabled = true;

            if (GUILayout.Button(showFriends ? "Hide list" : "By name", GUILayout.Width(70f)))
            {
                showFriends = !showFriends;

                if (showFriends)
                {
                    RefreshFriends();
                }
            }

            GUILayout.EndHorizontal();

            if (!SteamLobbyService.OverlayAvailable)
            {
                GUILayout.Label("No Steam overlay here — use the list.", Wrapped());
            }

            if (showFriends)
            {
                DrawFriends();
            }

            if (!string.IsNullOrEmpty(bootstrap.LastSteamMessage))
            {
                GUILayout.Label(bootstrap.LastSteamMessage, Wrapped());
            }
        }

        void DrawFriends()
        {
            if (GUILayout.Button("Refresh"))
            {
                RefreshFriends();
            }

            if (friends.Count == 0)
            {
                GUILayout.Label("No friends online.", Wrapped());
                return;
            }

            friendScroll = GUILayout.BeginScrollView(friendScroll, GUILayout.Height(110f));

            foreach (var friend in friends)
            {
                if (GUILayout.Button(friend.ToString()))
                {
                    bootstrap.Invite(friend.Id);
                }
            }

            GUILayout.EndScrollView();
        }

        /// <summary>
        /// Explicit rather than per-frame: <c>OnGUI</c> runs several times a frame, and walking the
        /// whole friends list through the Steam API each time would be a hundred native calls for a
        /// list that changes when somebody logs in.
        /// </summary>
        void RefreshFriends()
        {
            friends = SteamFriendList.Online();
        }

        static GUIStyle RichLabel()
        {
            return new GUIStyle(GUI.skin.label) { richText = true };
        }

        static GUIStyle Wrapped()
        {
            return new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 10 };
        }
    }
}
