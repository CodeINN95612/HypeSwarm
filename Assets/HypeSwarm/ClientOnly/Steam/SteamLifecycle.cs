using System;
using Steamworks;
using UnityEngine;

namespace HypeSwarm.ClientOnly.Steam
{
    /// <summary>
    /// Brings the Steam API up, pumps its callbacks, and takes it down again — and reports plainly
    /// when it cannot, because "cannot" is the normal case in the Editor.
    /// </summary>
    /// <remarks>
    /// Steam being absent is not an error. A developer with Steam closed, a dedicated server on a
    /// VPS (§13.4), and a player who launched the executable directly all reach this and all must
    /// end up on the direct transport with one line in the log rather than an exception. Every
    /// method here is safe to call when initialisation failed.
    ///
    /// <para><c>RestartAppIfNecessary</c> is deliberately not called. It relaunches the process
    /// through Steam, which during development means the Editor's player vanishing and reappearing
    /// under an app id that is not ours. It belongs with the real app id in Phase 5 (§22).</para>
    /// </remarks>
    public static class SteamLifecycle
    {
        /// <summary>
        /// Valve's public test app id. Every Steam account owns it, which is what makes it the
        /// standard stand-in until the game has an id of its own (§22).
        /// </summary>
        public const uint DevelopmentAppId = 480;

        static SteamCallbackPump pump;

        public static bool IsRunning { get; private set; }

        /// <summary>Why Steam is not running. Empty while it is.</summary>
        public static string FailureReason { get; private set; } = "Steam has not been started yet";

        /// <summary>This player's Steam id, or <c>CSteamID.Nil</c> when Steam is not running.</summary>
        public static CSteamID LocalUser => IsRunning ? SteamUser.GetSteamID() : CSteamID.Nil;

        /// <summary>This player's Steam persona name, or an empty string.</summary>
        public static string LocalName => IsRunning ? SteamFriends.GetPersonaName() : string.Empty;

        /// <summary>
        /// Starts Steam if it is not already started. Returns whether Steam is usable afterwards, so
        /// callers can treat a first call and a repeat call identically.
        /// </summary>
        public static bool TryStart()
        {
            if (IsRunning)
            {
                return true;
            }

            if (!Packsize.Test())
            {
                return Fail("the Steamworks binaries do not match this platform's struct packing");
            }

            if (!DllCheck.Test())
            {
                return Fail("the Steamworks native libraries are missing or the wrong version");
            }

            string error;

            // InitEx over Init: the same call, but it says what went wrong instead of just false.
            var result = SteamAPI.InitEx(out error);

            if (result != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
            {
                return Fail(string.IsNullOrEmpty(error) ? result.ToString() : error);
            }

            IsRunning = true;
            FailureReason = string.Empty;

            SteamClient.SetWarningMessageHook();
            EnsurePump();

            Debug.Log($"[Steam] Running as {LocalName} ({LocalUser}).");

            return true;
        }

        public static void Stop()
        {
            if (!IsRunning)
            {
                return;
            }

            IsRunning = false;
            FailureReason = "Steam was shut down";

            if (pump != null)
            {
                UnityEngine.Object.Destroy(pump.gameObject);
                pump = null;
            }

            SteamAPI.Shutdown();
        }

        static bool Fail(string reason)
        {
            IsRunning = false;
            FailureReason = reason;

            Debug.Log($"[Steam] Not available: {reason}. Falling back to direct connections.");

            return false;
        }

        /// <summary>
        /// Steam's callbacks only fire while something calls <c>RunCallbacks</c>, so initialising
        /// without a pump gives an API that appears to work and never answers.
        /// </summary>
        static void EnsurePump()
        {
            if (pump != null)
            {
                return;
            }

            var host = new GameObject("Steam Callbacks") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(host);

            pump = host.AddComponent<SteamCallbackPump>();
        }

        sealed class SteamCallbackPump : MonoBehaviour
        {
            void Update()
            {
                if (IsRunning)
                {
                    SteamAPI.RunCallbacks();
                }
            }

            void OnApplicationQuit() => Stop();
        }
    }

    /// <summary>Wraps the warning hook so the delegate is not collected while Steam holds it.</summary>
    static class SteamClient
    {
        static SteamAPIWarningMessageHook_t hook;

        public static void SetWarningMessageHook()
        {
            hook = OnWarning;
            SteamUtils.SetWarningMessageHook(hook);
        }

        [AOT.MonoPInvokeCallback(typeof(SteamAPIWarningMessageHook_t))]
        static void OnWarning(int severity, System.Text.StringBuilder message)
        {
            Debug.LogWarning($"[Steam] {message}");
        }
    }
}
