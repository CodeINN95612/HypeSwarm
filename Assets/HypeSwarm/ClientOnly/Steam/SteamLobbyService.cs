using System;
using Steamworks;
using UnityEngine;

namespace HypeSwarm.ClientOnly.Steam
{
    /// <summary>
    /// The Steam half of the invite flow: create a lobby, put the host's id in it, open the overlay
    /// so friends can be invited, and turn an accepted invite back into a host to connect to.
    /// </summary>
    /// <remarks>
    /// A Steam lobby is not the game session. It is a small piece of shared, discoverable state that
    /// solves the one problem a transport cannot solve for itself — how a friend learns which
    /// machine to connect to without anyone reading out an IP address. Everything the game cares
    /// about is in the two events below; the lobby itself is only the envelope.
    ///
    /// <para>Lobbies are friends-only. This is a co-op game with no matchmaking and no plan for any
    /// (§1), so a public lobby would advertise a session nobody is meant to find.</para>
    /// </remarks>
    public sealed class SteamLobbyService : IDisposable
    {
        /// <summary>Key under which the lobby carries the host's Steam id.</summary>
        public const string HostKey = "hypeswarm_host";

        /// <summary>Key identifying a lobby as ours, so a stray Spacewar lobby is not joined.</summary>
        public const string GameKey = "hypeswarm_game";

        public const string GameValue = "hype_swarm";

        Callback<GameLobbyJoinRequested_t> joinRequested;
        Callback<LobbyEnter_t> lobbyEntered;
        CallResult<LobbyCreated_t> lobbyCreated;

        /// <summary>The lobby this player is in, or nil.</summary>
        public CSteamID CurrentLobby { get; private set; } = CSteamID.Nil;

        /// <summary>Raised on the host once its lobby exists and is ready to be invited into.</summary>
        public event Action<CSteamID> LobbyCreated;

        /// <summary>
        /// Raised on a joining player with the Steam id of the host to connect to. This is the whole
        /// point of the lobby.
        /// </summary>
        public event Action<CSteamID> HostResolved;

        /// <summary>Raised when a lobby operation fails, with something worth showing a player.</summary>
        public event Action<string> Failed;

        public bool IsListening { get; private set; }

        /// <summary>
        /// Registers the Steam callbacks. Safe to call repeatedly; does nothing when Steam is not
        /// running.
        /// </summary>
        public bool TryListen()
        {
            if (IsListening)
            {
                return true;
            }

            if (!SteamLifecycle.TryStart())
            {
                return false;
            }

            joinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnJoinRequested);
            lobbyEntered = Callback<LobbyEnter_t>.Create(OnLobbyEntered);
            lobbyCreated = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
            IsListening = true;

            return true;
        }

        /// <summary>Creates a friends-only lobby with room for the party (spec §1).</summary>
        public bool TryCreateLobby(int maxMembers)
        {
            if (!TryListen())
            {
                Failed?.Invoke($"Steam is not available: {SteamLifecycle.FailureReason}.");
                return false;
            }

            LeaveLobby();

            var call = SteamMatchmaking.CreateLobby(
                ELobbyType.k_ELobbyTypeFriendsOnly, Mathf.Max(1, maxMembers));

            lobbyCreated.Set(call);

            return true;
        }

        /// <summary>
        /// Whether Steam's overlay is in this process. It is not injected into the Unity Editor, and
        /// not into a build Steam did not launch — which is most of development.
        /// </summary>
        public static bool OverlayAvailable => SteamLifecycle.IsRunning && SteamUtils.IsOverlayEnabled();

        /// <summary>
        /// Opens Steam's own invite dialog over the game. Using the overlay rather than a friend
        /// list of our own means invites, accepts, and the "join game" entry on a friend's profile
        /// all work without us implementing any of them.
        /// </summary>
        /// <remarks>
        /// <see cref="SteamFriends.ActivateGameOverlayInviteDialog"/> returns nothing and cannot
        /// fail — with no overlay in the process it draws no dialog and says nothing, which looks
        /// exactly like a dead button. The overlay is checked first so that case produces a sentence
        /// instead of silence.
        /// </remarks>
        public bool TryInviteFriends()
        {
            if (!HasLobby())
            {
                return false;
            }

            if (!OverlayAvailable)
            {
                Failed?.Invoke(
                    "The Steam overlay is not in this process, so the invite dialog cannot open. " +
                    "That is normal in the Editor and in a build Steam did not launch — invite a " +
                    "friend by name instead, or add the build to Steam as a non-Steam game.");

                return false;
            }

            SteamFriends.ActivateGameOverlayInviteDialog(CurrentLobby);

            return true;
        }

        /// <summary>
        /// Invites one friend directly, without the overlay. The invitee gets the same message in
        /// their Steam chat and the same <c>GameLobbyJoinRequested_t</c> when they accept, so this
        /// is the identical flow with a worse way of choosing who.
        /// </summary>
        public bool TryInvite(CSteamID friend)
        {
            if (!HasLobby())
            {
                return false;
            }

            if (!SteamMatchmaking.InviteUserToLobby(CurrentLobby, friend))
            {
                Failed?.Invoke(
                    $"Steam refused to invite {friend}. They may not be a friend of this account.");

                return false;
            }

            Debug.Log($"[Steam] Invited {friend} to lobby {CurrentLobby}.");

            return true;
        }

        bool HasLobby()
        {
            if (CurrentLobby != CSteamID.Nil)
            {
                return true;
            }

            Failed?.Invoke("There is no lobby to invite anyone into. Host on Steam first.");

            return false;
        }

        public void LeaveLobby()
        {
            if (CurrentLobby == CSteamID.Nil)
            {
                return;
            }

            SteamMatchmaking.LeaveLobby(CurrentLobby);
            CurrentLobby = CSteamID.Nil;
        }

        void OnLobbyCreated(LobbyCreated_t created, bool ioFailure)
        {
            if (ioFailure || created.m_eResult != EResult.k_EResultOK)
            {
                Failed?.Invoke($"Steam could not create a lobby ({(ioFailure ? "IO failure" : created.m_eResult.ToString())}).");
                return;
            }

            CurrentLobby = new CSteamID(created.m_ulSteamIDLobby);

            SteamMatchmaking.SetLobbyData(CurrentLobby, GameKey, GameValue);
            SteamMatchmaking.SetLobbyData(CurrentLobby, HostKey, SteamLifecycle.LocalUser.ToString());

            Debug.Log($"[Steam] Lobby {CurrentLobby} created. Friends can now be invited.");

            LobbyCreated?.Invoke(CurrentLobby);
        }

        /// <summary>
        /// Fires when a friend's invite is accepted, including from the Steam overlay or a launch
        /// argument, which is why joining is never driven from our own UI.
        /// </summary>
        void OnJoinRequested(GameLobbyJoinRequested_t request)
        {
            Debug.Log($"[Steam] Joining lobby {request.m_steamIDLobby} at a friend's invitation.");

            SteamMatchmaking.JoinLobby(request.m_steamIDLobby);
        }

        void OnLobbyEntered(LobbyEnter_t entered)
        {
            CurrentLobby = new CSteamID(entered.m_ulSteamIDLobby);

            // The host's own LobbyEnter for the lobby it just made. It is already serving.
            var hostId = SteamMatchmaking.GetLobbyData(CurrentLobby, HostKey);

            if (!ulong.TryParse(hostId, out var raw) || raw == 0)
            {
                Failed?.Invoke("That lobby does not name a Hype Swarm host. It may be a different game.");
                return;
            }

            var host = new CSteamID(raw);

            if (host == SteamLifecycle.LocalUser)
            {
                return;
            }

            HostResolved?.Invoke(host);
        }

        public void Dispose()
        {
            LeaveLobby();

            joinRequested?.Dispose();
            lobbyEntered?.Dispose();
            lobbyCreated?.Dispose();

            joinRequested = null;
            lobbyEntered = null;
            lobbyCreated = null;
            IsListening = false;
        }
    }
}
