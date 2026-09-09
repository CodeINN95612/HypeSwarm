using System;
using System.Collections.Generic;
using HypeSwarm.ClientOnly.Steam;
using HypeSwarm.Shared.Combat;
using HypeSwarm.Shared.Net;
using HypeSwarm.Shared.Stats;
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
        ChampionStats localStats;
        Health localHealth;

        /// <summary>
        /// The handful worth watching while testing. Not the stat screen — that is Phase 5 (§20).
        /// </summary>
        static readonly StatId[] WatchedStats =
        {
            StatId.MaxHealth,
            StatId.Damage,
            StatId.Haste,
            StatId.MoveSpeed,
            StatId.DamageReduction
        };

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

            using (new GUILayout.AreaScope(new Rect(10f, 10f, 320f, 760f), GUIContent.none, GUI.skin.box))
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

            DrawHealth();
            DrawStats();

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

        /// <summary>
        /// The local champion stats, as every machine currently resolves them.
        /// </summary>
        /// <remarks>
        /// This is how the exit criterion for Phase 2 step 4 gets watched rather than argued about:
        /// press the buff button on one client and the numbers move on all of them, because what
        /// travelled was the modifier list and each machine did its own arithmetic. If they ever
        /// disagree, they disagree here, in front of you.
        /// </remarks>
        void DrawStats()
        {
            var stats = LocalStats();

            GUILayout.Space(6f);

            if (stats == null)
            {
                GUILayout.Label("No champion spawned yet.", Wrapped());
                return;
            }

            var sheet = stats.Sheet;

            GUILayout.Label($"<b>Stats</b> — {sheet.Modifiers.Count} modifier(s)", RichLabel());

            for (var i = 0; i < WatchedStats.Length; i++)
            {
                DrawStat(sheet, WatchedStats[i]);
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var buff = ModifierSource.Parse(ChampionStats.DebugBuffId);
            var remaining = stats.RemainingOn(buff);

            if (GUILayout.Button(remaining > 0f ? $"Buff me ({remaining:0.#}s)" : "Buff me (10s)"))
            {
                stats.CmdDebugBuff(10f);
            }
#endif
        }

        /// <summary>
        /// The local champion health, as every machine currently sees it.
        /// </summary>
        /// <remarks>
        /// The exit criterion for Phase 2 step 5 in one readout: hit the button and the bar drops on
        /// every machine, a shield eats the next hit before health does, regeneration comes back on
        /// its own after the delay, and zero reads as dead rather than as a negative number.
        ///
        /// <para>Health replicates as values while stats replicate as modifiers, and this is where
        /// that shows: the number below was computed on the host and sent, because the damage events
        /// behind it were never sent at all.</para>
        /// </remarks>
        void DrawHealth()
        {
            var health = LocalHealth();

            GUILayout.Space(6f);

            if (health == null)
            {
                return;
            }

            var shield = health.Shield;

            GUILayout.Label(
                health.IsDead
                    ? "<b>Health</b> — dead"
                    : $"<b>Health</b> — {health.Current:0.#} / {health.Max:0.#}{(shield > 0f ? $"  (+{shield:0.#} shield)" : string.Empty)}",
                RichLabel());

            var bar = GUILayoutUtility.GetRect(10f, 10f, GUILayout.ExpandWidth(true));

            GUI.Box(bar, GUIContent.none);
            GUI.Box(new Rect(bar.x, bar.y, bar.width * Clamped(health.Fraction), bar.height), GUIContent.none);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Hit 25"))
            {
                health.CmdDebugDamage(25f);
            }

            if (GUILayout.Button("Shield 50"))
            {
                health.CmdDebugShield(50f, 10f);
            }

            GUI.enabled = health.IsDead;

            if (GUILayout.Button("Revive"))
            {
                health.CmdDebugRevive();
            }

            GUI.enabled = true;

            GUILayout.EndHorizontal();
#endif
        }

        static float Clamped(float fraction)
        {
            return fraction < 0f ? 0f : fraction > 1f ? 1f : fraction;
        }

        Health LocalHealth()
        {
            if (localHealth != null)
            {
                return localHealth;
            }

            var player = NetworkClient.localPlayer;

            localHealth = player == null ? null : player.GetComponent<Health>();

            return localHealth;
        }

        static void DrawStat(StatSheet sheet, StatId stat)
        {
            var definition = sheet.Catalog[stat];
            var value = sheet.Get(stat);

            GUILayout.Label(
                definition.IsDerived
                    ? $"{definition.DisplayName}: {value:0.#} → {sheet.Effect(stat) * 100f:0.#}%"
                    : $"{definition.DisplayName}: {value:0.#}",
                Wrapped());
        }

        /// <summary>
        /// The champion this machine owns. Looked up rather than wired: it does not exist until the
        /// server spawns it, and it is gone again on disconnect.
        /// </summary>
        ChampionStats LocalStats()
        {
            if (localStats != null)
            {
                return localStats;
            }

            var player = NetworkClient.localPlayer;

            localStats = player == null ? null : player.GetComponent<ChampionStats>();

            return localStats;
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
