using System;
using System.Collections.Generic;

namespace HypeSwarm.Shared.Net
{
    /// <summary>One player's place in the lobby.</summary>
    public readonly struct LobbySlot : IEquatable<LobbySlot>
    {
        /// <summary>Mirror's connection id. <c>0</c> is the host's own local connection.</summary>
        public int ConnectionId { get; }

        /// <summary>Stable index in <c>[0, Capacity)</c>. Reused when someone leaves.</summary>
        public int Index { get; }

        public string DisplayName { get; }

        public LobbySlot(int connectionId, int index, string displayName)
        {
            ConnectionId = connectionId;
            Index = index;
            DisplayName = displayName ?? string.Empty;
        }

        public bool Equals(LobbySlot other) =>
            ConnectionId == other.ConnectionId && Index == other.Index &&
            string.Equals(DisplayName, other.DisplayName, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is LobbySlot other && Equals(other);

        public override int GetHashCode() => (ConnectionId * 397) ^ Index;

        public override string ToString() => $"[{Index}] {DisplayName} (conn {ConnectionId})";
    }

    /// <summary>
    /// Who is in the lobby, held on the host. Pure bookkeeping — no Mirror types, no scene, so the
    /// join and leave rules can be tested without five processes.
    /// </summary>
    /// <remarks>
    /// Mirror's <c>maxConnections</c> already refuses the sixth socket; this exists for the part
    /// Mirror does not do — giving each player a stable index that survives other players leaving.
    /// Slot index is what colours, HUD ordering, and spawn points will key off, so a player who was
    /// second must not become third because the first person quit.
    ///
    /// <para>Indices are reused lowest-first. Five slots and a sixty-minute session means people
    /// will drop and rejoin, and a roster that only ever counted upward would hand out index 7 in a
    /// five-player game.</para>
    /// </remarks>
    public sealed class LobbyRoster
    {
        /// <summary>1–5 players (spec §1). The number the whole design is balanced around.</summary>
        public const int MaxPlayers = 5;

        readonly List<LobbySlot> members = new List<LobbySlot>();
        readonly bool[] taken;

        public LobbyRoster(int capacity = MaxPlayers)
        {
            Capacity = Math.Max(1, capacity);
            taken = new bool[Capacity];
        }

        public int Capacity { get; }

        public int Count => members.Count;

        public bool IsFull => members.Count >= Capacity;

        /// <summary>Members in join order. Read <see cref="LobbySlot.Index"/> for the stable position.</summary>
        public IReadOnlyList<LobbySlot> Members => members;

        public event Action<LobbySlot> Joined;

        public event Action<LobbySlot> Left;

        public bool Contains(int connectionId) => IndexOf(connectionId) >= 0;

        /// <summary>
        /// Adds a player. Returns false when the lobby is full or that connection is already in it —
        /// a duplicate is a bug somewhere upstream, and admitting it twice would hand out two slots
        /// to one socket and leak one of them on disconnect.
        /// </summary>
        public bool TryAdd(int connectionId, string displayName, out LobbySlot slot)
        {
            slot = default;

            if (IsFull || Contains(connectionId))
            {
                return false;
            }

            var index = FirstFreeIndex();

            if (index < 0)
            {
                return false;
            }

            taken[index] = true;
            slot = new LobbySlot(connectionId, index, displayName);
            members.Add(slot);
            Joined?.Invoke(slot);

            return true;
        }

        public bool TryRemove(int connectionId, out LobbySlot slot)
        {
            var position = IndexOf(connectionId);

            if (position < 0)
            {
                slot = default;
                return false;
            }

            slot = members[position];
            members.RemoveAt(position);
            taken[slot.Index] = false;
            Left?.Invoke(slot);

            return true;
        }

        public bool TryGet(int connectionId, out LobbySlot slot)
        {
            var position = IndexOf(connectionId);
            slot = position < 0 ? default : members[position];

            return position >= 0;
        }

        /// <summary>Empties the roster without raising <see cref="Left"/>. For shutting a server down.</summary>
        public void Clear()
        {
            members.Clear();
            Array.Clear(taken, 0, taken.Length);
        }

        int FirstFreeIndex()
        {
            for (var i = 0; i < taken.Length; i++)
            {
                if (!taken[i])
                {
                    return i;
                }
            }

            return -1;
        }

        int IndexOf(int connectionId)
        {
            for (var i = 0; i < members.Count; i++)
            {
                if (members[i].ConnectionId == connectionId)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
