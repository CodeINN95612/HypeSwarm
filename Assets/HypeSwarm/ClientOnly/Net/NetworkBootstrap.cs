using System;
using HypeSwarm.ClientOnly.Steam;
using HypeSwarm.Shared.Net;
using kcp2k;
using Mirror;
using Mirror.FizzySteam;
using Steamworks;
using UnityEngine;

namespace HypeSwarm.ClientOnly.Net
{
    /// <summary>
    /// Decides how this process joins the world: which transport it uses, and whether it hosts,
    /// connects, serves, or does nothing.
    /// </summary>
    /// <remarks>
    /// This is where spec §13.4 is actually honoured. Both transports sit on the same object and
    /// neither is wired into the network manager in the scene; the choice is made here, at runtime,
    /// from the command line and from whether a Steam client answered. Nothing else in the codebase
    /// names a transport, which is the property that makes a dedicated server (§23) a configuration
    /// change rather than a port.
    ///
    /// <para>It also decides what to do on startup, so that pressing Play still puts a champion on
    /// the ground with no further ceremony — the prototype from step 2 with a network underneath it,
    /// rather than a menu to click through every time.</para>
    /// </remarks>
    [DefaultExecutionOrder(-100)]
    public sealed class NetworkBootstrap : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField]
        HypeSwarmNetworkManager manager;

        [SerializeField]
        [Tooltip("KCP over UDP. Always available; the fallback when Steam is not.")]
        KcpTransport directTransport;

        [SerializeField]
        [Tooltip("Steam relay. Requires a running Steam client, so never on a dedicated server.")]
        FizzySteamworks steamTransport;

        [Header("Startup")]
        [SerializeField]
        [Tooltip("Ask Steam whether it is running when this scene loads. Turn off to keep the " +
                 "Editor from touching Steam at all while working on something else.")]
        bool probeSteamOnStart = true;

        [SerializeField]
        [Tooltip("Act on the launch arguments when the scene loads. Off means nothing starts until " +
                 "a button is pressed.")]
        bool startOnLoad = true;

        SteamLobbyService lobby;

        /// <summary>The transport in use and why. Valid after <c>Start</c>.</summary>
        public TransportSelection Selection { get; private set; }

        /// <summary>What the command line asked for. Valid after <c>Start</c>.</summary>
        public NetworkLaunchOptions Options { get; private set; }

        /// <summary>The Steam lobby, created on demand. Null until Steam is used.</summary>
        public SteamLobbyService Lobby => lobby;

        void Awake()
        {
            if (manager == null)
            {
                manager = GetComponent<HypeSwarmNetworkManager>();
            }

            Options = NetworkLaunchOptions.Parse(Environment.GetCommandLineArgs());

            foreach (var warning in Options.Warnings)
            {
                Debug.LogWarning($"[Net] {warning}");
            }
        }

        void Start()
        {
            if (manager == null)
            {
                Debug.LogError("NetworkBootstrap has no network manager, so nothing can start.", this);
                return;
            }

            manager.LocalDisplayName = ResolveDisplayName();

            Selection = TransportSelector.Select(Options, ProbeAvailability());
            Apply(Selection.Kind);

            Debug.Log($"[Net] {Selection}");

            if (startOnLoad)
            {
                Begin(Options.ResolvedRole);
            }
        }

        void OnDestroy()
        {
            lobby?.Dispose();
            lobby = null;
        }

        // --- Transport ---------------------------------------------------------------------

        TransportAvailability ProbeAvailability()
        {
            if (Options.Headless)
            {
                return TransportAvailability.DirectOnly("this is a headless process");
            }

            if (!probeSteamOnStart)
            {
                return TransportAvailability.DirectOnly("Steam probing is turned off on the bootstrap");
            }

            if (steamTransport == null)
            {
                return TransportAvailability.DirectOnly("no Steam transport is wired up in the scene");
            }

            return SteamLifecycle.TryStart()
                ? TransportAvailability.WithSteam()
                : TransportAvailability.DirectOnly(SteamLifecycle.FailureReason);
        }

        /// <summary>
        /// Installs a transport. Only one is enabled at a time — two live transports both pumping
        /// their sockets is a source of confusion out of all proportion to the cost of a bool.
        /// </summary>
        void Apply(TransportKind kind)
        {
            var steam = kind == TransportKind.Steam && steamTransport != null;
            var chosen = steam ? (Transport)steamTransport : directTransport;

            if (chosen == null)
            {
                Debug.LogError($"No {kind} transport is wired up on {name}.", this);
                return;
            }

            if (directTransport != null)
            {
                directTransport.enabled = !steam;
                directTransport.port = Options.Port;
            }

            if (steamTransport != null)
            {
                steamTransport.enabled = steam;
            }

            manager.transport = chosen;
            Transport.active = chosen;
        }

        /// <summary>
        /// Switches transport, stopping anything already running. Mirror binds the active transport
        /// when a session starts, so changing it underneath a live connection would leave the
        /// manager talking to a socket nobody is listening on.
        /// </summary>
        public void SwitchTransport(TransportKind kind)
        {
            if (kind == TransportKind.Steam && !SteamLifecycle.TryStart())
            {
                Debug.LogWarning($"[Net] Steam is not available: {SteamLifecycle.FailureReason}.");
                return;
            }

            Stop();
            Selection = new TransportSelection(kind, "chosen from the network panel");
            Apply(kind);
        }

        // --- Sessions ----------------------------------------------------------------------

        void Begin(NetworkRole role)
        {
            switch (role)
            {
                case NetworkRole.Host:
                    manager.StartHost();
                    break;

                case NetworkRole.Server:
                    manager.StartServer();
                    break;

                case NetworkRole.Client:
                    JoinDirect(Options.Address, Options.Port);
                    break;

                case NetworkRole.Offline:
                    Debug.Log("[Net] Started offline; nothing is listening.");
                    break;
            }
        }

        public void HostDirect()
        {
            Stop();
            Apply(TransportKind.Direct);
            manager.StartHost();
        }

        public void JoinDirect(string address, ushort port)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                Debug.LogWarning("[Net] No address to connect to.");
                return;
            }

            Stop();
            Apply(TransportKind.Direct);

            if (directTransport != null)
            {
                directTransport.port = port;
            }

            manager.networkAddress = address.Trim();
            manager.StartClient();
        }

        /// <summary>
        /// Hosts through Steam: create the lobby first, then start serving once it exists. Doing it
        /// in that order means the invite button is live the moment the game is, rather than
        /// pointing at a lobby that does not exist yet.
        /// </summary>
        public void HostOnSteam()
        {
            if (!EnsureLobby())
            {
                return;
            }

            Stop();
            Apply(TransportKind.Steam);

            lobby.TryCreateLobby(LobbyRoster.MaxPlayers);
        }

        public void InviteFriends()
        {
            if (EnsureLobby())
            {
                lobby.TryInviteFriends();
            }
        }

        public void Stop()
        {
            if (NetworkServer.active && NetworkClient.isConnected)
            {
                manager.StopHost();
            }
            else if (NetworkServer.active)
            {
                manager.StopServer();
            }
            else if (NetworkClient.active)
            {
                manager.StopClient();
            }
        }

        bool EnsureLobby()
        {
            if (lobby == null)
            {
                lobby = new SteamLobbyService();
                lobby.LobbyCreated += OnLobbyCreated;
                lobby.HostResolved += OnHostResolved;
                lobby.Failed += reason => Debug.LogWarning($"[Steam] {reason}");
            }

            if (lobby.TryListen())
            {
                return true;
            }

            Debug.LogWarning($"[Steam] Not available: {SteamLifecycle.FailureReason}.");

            return false;
        }

        void OnLobbyCreated(CSteamID lobbyId)
        {
            if (!NetworkServer.active)
            {
                manager.StartHost();
            }
        }

        /// <summary>
        /// A friend's invite was accepted. The lobby carried the host's Steam id, and for Fizzy that
        /// id <i>is</i> the address — no IP, no port, no NAT to traverse.
        /// </summary>
        void OnHostResolved(CSteamID host)
        {
            Stop();
            Apply(TransportKind.Steam);

            manager.networkAddress = host.ToString();
            manager.StartClient();
        }

        string ResolveDisplayName()
        {
            if (!string.IsNullOrWhiteSpace(Options.DisplayName))
            {
                return Options.DisplayName;
            }

            var steamName = SteamLifecycle.LocalName;

            return string.IsNullOrWhiteSpace(steamName) ? SystemInfo.deviceName : steamName;
        }
    }
}
