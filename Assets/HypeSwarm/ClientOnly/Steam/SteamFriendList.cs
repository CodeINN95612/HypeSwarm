using System.Collections.Generic;
using Steamworks;

namespace HypeSwarm.ClientOnly.Steam
{
    /// <summary>One entry in the friends list: enough to show a name and send an invite.</summary>
    public readonly struct SteamFriend
    {
        public SteamFriend(CSteamID id, string name, EPersonaState state, bool inThisGame)
        {
            Id = id;
            Name = name;
            State = state;
            InThisGame = inThisGame;
        }

        public CSteamID Id { get; }

        public string Name { get; }

        public EPersonaState State { get; }

        /// <summary>Whether they are running the same app id, which for now is Spacewar (§22).</summary>
        public bool InThisGame { get; }

        public bool IsOnline => State != EPersonaState.k_EPersonaStateOffline;

        public override string ToString() =>
            InThisGame ? $"{Name} (in game)" : Name;
    }

    /// <summary>
    /// Reads the local player's friends list.
    /// </summary>
    /// <remarks>
    /// This exists only because the Steam overlay is not always in the process — it is not injected
    /// into the Unity Editor at all, and not into a build that Steam did not launch. Without it,
    /// <see cref="SteamFriends.ActivateGameOverlayInviteDialog"/> draws nothing, and there is no
    /// second way to name the person being invited. A list of our own is that second way.
    ///
    /// <para>Nothing here is meant to survive into the real UI (§20). The invite the player uses
    /// will be Steam's own dialog, which is better than anything drawn here and costs nothing to
    /// maintain.</para>
    /// </remarks>
    public static class SteamFriendList
    {
        /// <summary>
        /// Friends who could plausibly be invited, online first. Returns an empty list rather than
        /// null when Steam is not running, so callers never branch on it.
        /// </summary>
        public static IReadOnlyList<SteamFriend> Online(bool includeOffline = false)
        {
            var friends = new List<SteamFriend>();

            if (!SteamLifecycle.IsRunning)
            {
                return friends;
            }

            // Immediate: actual friends, not group members or people met in a lobby. Inviting any of
            // the others would fail, and a list of names that cannot be clicked is worse than a
            // short list.
            var count = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);

            for (var i = 0; i < count; i++)
            {
                var id = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
                var state = SteamFriends.GetFriendPersonaState(id);

                if (state == EPersonaState.k_EPersonaStateOffline && !includeOffline)
                {
                    continue;
                }

                var inGame = SteamFriends.GetFriendGamePlayed(id, out var game)
                             && game.m_gameID.AppID() == SteamUtils.GetAppID();

                friends.Add(new SteamFriend(id, SteamFriends.GetFriendPersonaName(id), state, inGame));
            }

            // Whoever is already running the game is the one being tested with; putting them at the
            // top saves scrolling past a hundred names on every attempt.
            friends.Sort((left, right) =>
            {
                if (left.InThisGame != right.InThisGame)
                {
                    return left.InThisGame ? -1 : 1;
                }

                return string.Compare(left.Name, right.Name, System.StringComparison.OrdinalIgnoreCase);
            });

            return friends;
        }
    }
}
