using System;
using Mirror;
using UnityEngine;

namespace HypeSwarm.Shared.Net
{
    /// <summary>
    /// The session: who is in it, where they spawn, and what happens when they leave.
    /// </summary>
    /// <remarks>
    /// This lives in <c>Shared</c> rather than <c>ClientOnly</c> because a dedicated server runs it
    /// with no camera and no window (spec §23). Nothing here touches presentation; the dev panel
    /// and the Steam lobby are separate components that talk to it.
    ///
    /// <para>Player creation is explicit rather than automatic (see <see cref="JoinRequestMessage"/>).
    /// Mirror's <c>autoCreatePlayer</c> spawns on connect, which would put the champion in the world
    /// before the host had decided whether this connection is welcome.</para>
    /// </remarks>
    [AddComponentMenu("Hype Swarm/Network Manager")]
    public class HypeSwarmNetworkManager : NetworkManager
    {
        /// <summary>The roster, valid on the host. Empty on a pure client.</summary>
        public LobbyRoster Roster { get; } = new LobbyRoster();

        /// <summary>Raised on a client when the host answers its join request.</summary>
        public event Action<JoinResultMessage> JoinAnswered;

        /// <summary>This client's slot index once accepted, or -1.</summary>
        public int LocalSlotIndex { get; private set; } = -1;

        /// <summary>Name this client asks to be known by. Set before connecting.</summary>
        public string LocalDisplayName { get; set; } = string.Empty;

        public override void Awake()
        {
            // Both are enforced again below, but setting them here means the inspector cannot show
            // a number that disagrees with the design.
            maxConnections = LobbyRoster.MaxPlayers;
            autoCreatePlayer = false;

            base.Awake();
        }

        // --- Server ------------------------------------------------------------------------

        public override void OnStartServer()
        {
            base.OnStartServer();

            Roster.Clear();
            NetworkServer.RegisterHandler<JoinRequestMessage>(OnJoinRequested);
        }

        public override void OnStopServer()
        {
            Roster.Clear();
            base.OnStopServer();
        }

        void OnJoinRequested(NetworkConnectionToClient connection, JoinRequestMessage message)
        {
            if (connection.identity != null)
            {
                // A second request from a connection that already has a champion. Answering rather
                // than ignoring keeps a confused client from waiting forever for a reply.
                Refuse(connection, "you already have a champion in this session");
                return;
            }

            var name = SanitiseName(message.DisplayName, connection.connectionId);

            if (!Roster.TryAdd(connection.connectionId, name, out var slot))
            {
                Refuse(connection, Roster.IsFull
                    ? $"the lobby is full ({Roster.Capacity} players)"
                    : "that connection is already in the lobby");
                return;
            }

            var spawn = SpawnPointFor(slot.Index);
            var champion = Instantiate(playerPrefab, spawn.Position, spawn.Rotation);
            champion.name = $"Champion {slot.Index} ({slot.DisplayName})";

            NetworkServer.AddPlayerForConnection(connection, champion);

            connection.Send(new JoinResultMessage
            {
                Accepted = true,
                SlotIndex = slot.Index,
                Reason = string.Empty
            });

            Debug.Log($"[Net] {slot} joined. {Roster.Count}/{Roster.Capacity} in the lobby.");
        }

        static void Refuse(NetworkConnectionToClient connection, string reason)
        {
            connection.Send(new JoinResultMessage
            {
                Accepted = false,
                SlotIndex = -1,
                Reason = reason
            });

            Debug.Log($"[Net] Refused connection {connection.connectionId}: {reason}.");
        }

        public override void OnServerDisconnect(NetworkConnectionToClient connection)
        {
            if (Roster.TryRemove(connection.connectionId, out var slot))
            {
                Debug.Log($"[Net] {slot} left. {Roster.Count}/{Roster.Capacity} in the lobby.");
            }

            // Destroys the champion and cleans up the connection.
            base.OnServerDisconnect(connection);
        }

        /// <summary>
        /// Spawn by slot rather than at random. The same slot always gets the same point, so a
        /// player who reconnects appears where they were and five players never stack on one tile.
        /// </summary>
        (Vector3 Position, Quaternion Rotation) SpawnPointFor(int slotIndex)
        {
            var points = startPositions;

            for (var i = points.Count - 1; i >= 0; i--)
            {
                if (points[i] == null)
                {
                    points.RemoveAt(i);
                }
            }

            if (points.Count == 0)
            {
                return (Vector3.zero, Quaternion.identity);
            }

            var point = points[Mathf.Abs(slotIndex) % points.Count];

            return (point.position, point.rotation);
        }

        static string SanitiseName(string requested, int connectionId)
        {
            if (string.IsNullOrWhiteSpace(requested))
            {
                return $"Player {connectionId}";
            }

            var trimmed = requested.Trim();

            return trimmed.Length <= 24 ? trimmed : trimmed.Substring(0, 24);
        }

        // --- Client ------------------------------------------------------------------------

        public override void OnStartClient()
        {
            base.OnStartClient();

            LocalSlotIndex = -1;
            NetworkClient.RegisterHandler<JoinResultMessage>(OnJoinAnswered);
        }

        public override void OnClientConnect()
        {
            base.OnClientConnect();

            NetworkClient.Send(new JoinRequestMessage { DisplayName = LocalDisplayName });
        }

        public override void OnStopClient()
        {
            LocalSlotIndex = -1;
            base.OnStopClient();
        }

        void OnJoinAnswered(JoinResultMessage message)
        {
            LocalSlotIndex = message.Accepted ? message.SlotIndex : -1;

            if (!message.Accepted)
            {
                Debug.LogWarning($"[Net] The host refused the join: {message.Reason}.");
            }

            JoinAnswered?.Invoke(message);
        }
    }
}
